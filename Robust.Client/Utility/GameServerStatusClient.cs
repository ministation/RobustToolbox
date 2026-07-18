// SPDX-License-Identifier: MIT

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Robust.Client.Utility;

internal sealed class GameServerStatusClient : IGameServerStatusClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    // Resolve concrete holder — IHttpClientHolder is intentionally not registered on the client.
    [Dependency] private readonly HttpClientHolder _http = default!;

    public async Task<GameServerStatusInfo?> FetchStatusAsync(
        string? host,
        int? port,
        string? ss14OrConnectAddress,
        CancellationToken cancel = default)
    {
        if (!TryBuildStatusUri(host, port, ss14OrConnectAddress, out var uri))
            return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        cts.CancelAfter(RequestTimeout);

        try
        {
            using var response = await _http.Client.GetAsync(uri, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);
            var root = doc.RootElement;

            return new GameServerStatusInfo(
                Name: TryGetString(root, "name"),
                Players: TryGetInt(root, "players"),
                SoftMaxPlayers: TryGetInt(root, "soft_max_players"),
                Map: TryGetString(root, "map"),
                Preset: TryGetString(root, "preset"));
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.DebugS("status", "Failed to fetch {0}: {1}", uri, e.Message);
            return null;
        }
    }

    /// <summary>
    /// Builds a status URL. Prefers ss14(s) launcher rules (ports 1211/1212), then host:port.
    /// </summary>
    internal static bool TryBuildStatusUri(string? host, int? port, string? address, out Uri uri)
    {
        uri = default!;

        if (!string.IsNullOrWhiteSpace(address))
        {
            var trimmed = address.Trim();

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var connectUri) &&
                (connectUri.Scheme == "ss14" || connectUri.Scheme == "ss14s"))
            {
                try
                {
                    uri = GetServerStatusAddress(connectUri);
                    return true;
                }
                catch (ArgumentException e)
                {
                    Logger.DebugS("status", "Invalid connect address for status: {0}", e.Message);
                }
            }
        }

        var resolvedHost = host;
        var resolvedPort = port;

        if ((string.IsNullOrWhiteSpace(resolvedHost) || resolvedPort is null or <= 0) &&
            !string.IsNullOrWhiteSpace(address))
        {
            var trimmed = address.Trim();

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) &&
                parsed.Scheme is "http" or "https" or "udp")
            {
                resolvedHost = parsed.Host;
                resolvedPort = parsed.IsDefaultPort
                    ? (parsed.Scheme == "https" ? 1212 : 1211)
                    : parsed.Port;
            }
            else
            {
                // Plain "host:port" from --connect-address
                var colon = trimmed.LastIndexOf(':');
                if (colon > 0 &&
                    int.TryParse(trimmed.AsSpan(colon + 1), out var parsedPort) &&
                    parsedPort > 0)
                {
                    resolvedHost = trimmed[..colon].Trim('[', ']');
                    resolvedPort = parsedPort;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedHost) || resolvedPort is null or <= 0)
            return false;

        uri = new UriBuilder("http", resolvedHost, resolvedPort.Value, "/status").Uri;
        return true;
    }

    /// <summary>
    /// Same rules as SS14.Launcher <c>UriHelper.GetServerStatusAddress</c>.
    /// </summary>
    private static Uri GetServerStatusAddress(Uri connectUri)
    {
        DebugTools.Assert(connectUri.Scheme == "ss14" || connectUri.Scheme == "ss14s");

        var scheme = connectUri.Scheme == "ss14s" ? "https" : "http";
        var statusPort = connectUri.IsDefaultPort
            ? connectUri.Scheme == "ss14s" ? 1212 : 1211
            : connectUri.Port;

        var builder = new UriBuilder(connectUri)
        {
            Scheme = scheme,
            Port = statusPort,
        };

        return new Uri(builder.Uri, "status");
    }

    private static string? TryGetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return null;

        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
    }

    private static int? TryGetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop))
            return null;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var i))
            return i;

        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out i))
            return i;

        return null;
    }
}
