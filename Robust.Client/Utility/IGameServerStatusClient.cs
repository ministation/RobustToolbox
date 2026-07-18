// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Robust.Client.Utility;

/// <summary>
/// Fetches the public HTTP /status JSON from a game server.
/// Implemented in the engine so content stays inside the client sandbox
/// (content assemblies may not use <c>System.Net.Http</c>).
/// </summary>
public interface IGameServerStatusClient
{
    /// <summary>
    /// GET http://host:port/status. Pass either host+port and/or an ss14(s):// / host:port address string.
    /// </summary>
    Task<GameServerStatusInfo?> FetchStatusAsync(
        string? host,
        int? port,
        string? ss14OrConnectAddress,
        CancellationToken cancel = default);
}

/// <summary>
/// Snapshot of common fields from the engine status host / GameTicker StatusShell.
/// </summary>
public readonly record struct GameServerStatusInfo(
    string? Name,
    int? Players,
    int? SoftMaxPlayers,
    string? Map,
    string? Preset);
