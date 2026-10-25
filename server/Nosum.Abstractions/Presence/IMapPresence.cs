// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Gameplay.Movement;

namespace Nosum.Abstractions.Presence;

/// <summary>
/// Who stands on which map. A player is on exactly one map at a time; every map is one Nalix
/// connection group (<see cref="MapGroup.Name"/>). Feature-agnostic: movement, farming and chat
/// all use it to find the players of a map and to send to them.
/// </summary>
public interface IMapPresence
{
    /// <summary>Raised after a player left a map, through a leave request, a map change or a disconnect.</summary>
    event Func<PlayerPresence, ValueTask>? PlayerLeft;

    /// <summary>Gets the number of players on all maps.</summary>
    int PlayerCount { get; }

    /// <summary>Puts a connection on a map, first removing it from the map it was on.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="accountId">The signed-in account behind the connection.</param>
    /// <param name="mapId">Map key.</param>
    /// <param name="motion">Starting position, facing and movement budget.</param>
    /// <param name="playerId">The id to keep when the player changes map, or <see langword="null"/> for a new id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new presence record.</returns>
    ValueTask<PlayerPresence> JoinAsync(IConnection connection, Guid accountId, string mapId, MotionState motion, uint? playerId = null, CancellationToken cancellationToken = default);

    /// <summary>Removes a connection from its map; does nothing when it is on none.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the player is gone and <see cref="PlayerLeft"/> handlers ran.</returns>
    ValueTask LeaveAsync(IConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Finds the presence of a connection.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="player">The record when the connection is on a map.</param>
    /// <returns><see langword="true"/> when found.</returns>
    bool TryGet(IConnection connection, [NotNullWhen(true)] out PlayerPresence? player);

    /// <summary>Appends the ids of maps that have at least one player to a caller-owned list.</summary>
    /// <param name="into">List that receives the ids.</param>
    void CopyActiveMapIds(List<string> into);

    /// <summary>Appends the players of one map to a caller-owned list.</summary>
    /// <param name="mapId">Map key.</param>
    /// <param name="into">List that receives the players.</param>
    void CopyPlayers(string mapId, List<PlayerPresence> into);

    /// <summary>Sends one packet to one connection, with the same transport and encryption as <see cref="MulticastAsync"/>.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="packet">The packet; the caller keeps ownership and may return it to its pool afterwards.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the packet was sent.</returns>
    ValueTask SendAsync(IConnection connection, IPacket packet, CancellationToken cancellationToken = default);

    /// <summary>Sends one packet to every player of a map. Best effort: a dead connection never fails the rest.</summary>
    /// <param name="mapId">Map key.</param>
    /// <param name="packet">The packet; the caller keeps ownership and may return it to its pool afterwards.</param>
    /// <param name="except">A connection to skip, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when every send finished.</returns>
    ValueTask MulticastAsync(string mapId, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default);
}
