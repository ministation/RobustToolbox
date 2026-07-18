// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Robust.Client.Utility;

/// <summary>
/// Fetches the public HTTP <c>/status</c> JSON from a game server.
/// Implemented in the engine so content stays inside the client sandbox
/// (content assemblies may not use <c>System.Net.Http</c>).
/// </summary>
[PublicAPI]
public interface IGameServerStatusClient
{
    /// <summary>
    /// GET http(s)://host:port/status. Prefer <paramref name="ss14OrConnectAddress"/> when
    /// connecting via the launcher (<c>ss14://</c> / <c>ss14s://</c> / host:port);
    /// otherwise pass host/port from the active net session.
    /// </summary>
    Task<GameServerStatusInfo?> FetchStatusAsync(
        string? host,
        int? port,
        string? ss14OrConnectAddress,
        CancellationToken cancel = default);
}

/// <summary>
/// Parsed fields from a game server <c>/status</c> response.
/// </summary>
[PublicAPI]
public readonly record struct GameServerStatusInfo(
    string? Name,
    int? Players,
    int? SoftMaxPlayers,
    string? Map,
    string? Preset);
