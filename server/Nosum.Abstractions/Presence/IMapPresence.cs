// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;

namespace Nosum.Abstractions.Presence;

/// <summary>
/// Who stands where. A player is in exactly one map instance at a time; every instance is one Nalix connection
/// group (<see cref="MapGroup.Name"/>). Feature-agnostic: movement, farming, looks and chat all use it to find the
/// players of a place and to send to them. Places are named by their instance key (<see cref="MapInstance.Key"/>):
/// for a shared map that is the map id, for a player's private farm or farmhouse it is the map id plus the owner
/// (docs/adr/ADR-015), so "the players on the same map" is always "the players with the same key" and never leaks
/// across private copies. Use <see cref="PlayerPresence.Key"/> to address the place a player is in.
/// </summary>
public interface IMapPresence
{
    /// <summary>Raised after a player left a map, through a leave request, a map change or a disconnect.</summary>
    event Func<PlayerPresence, ValueTask>? PlayerLeft;

    /// <summary>
    /// Raised after a player joined a map (a sign-in, a map change). The default does nothing, so a test double need not implement it; the server's
    /// presence raises it (docs/adr/ADR-029 D4: friends see each other's public map).
    /// </summary>
    event Func<PlayerPresence, ValueTask>? PlayerJoined
    {
        add { }
        remove { }
    }

    /// <summary>Gets the number of players on all maps.</summary>
    int PlayerCount { get; }

    /// <summary>Puts a connection in a map instance, first removing it from the place it was in.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="accountId">The signed-in account behind the connection.</param>
    /// <param name="instance">The place: a shared map, or one player's private copy of a map.</param>
    /// <param name="motion">Starting position, facing and movement budget.</param>
    /// <param name="playerId">The id to keep when the player changes map, or <see langword="null"/> for a new id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new presence record.</returns>
    ValueTask<PlayerPresence> JoinAsync(IConnection connection, Guid accountId, MapInstance instance, MotionState motion, uint? playerId = null, CancellationToken cancellationToken = default);

    /// <summary>Puts a connection on a shared map; the same as joining <see cref="MapInstance.Shared"/>.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="accountId">The signed-in account behind the connection.</param>
    /// <param name="mapId">Id of a shared map.</param>
    /// <param name="motion">Starting position, facing and movement budget.</param>
    /// <param name="playerId">The id to keep, or <see langword="null"/> for a new id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new presence record.</returns>
    ValueTask<PlayerPresence> JoinAsync(IConnection connection, Guid accountId, string mapId, MotionState motion, uint? playerId = null, CancellationToken cancellationToken = default)
        => this.JoinAsync(connection, accountId, MapInstance.Shared(mapId), motion, playerId, cancellationToken);

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

    /// <summary>Appends the keys of instances that have at least one player to a caller-owned list.</summary>
    /// <param name="into">List that receives the ids.</param>
    void CopyActiveMapIds(List<string> into);

    /// <summary>Appends the players of one instance to a caller-owned list.</summary>
    /// <param name="instanceKey">Instance key (<see cref="MapInstance.Key"/>); the map id for a shared map.</param>
    /// <param name="into">List that receives the players.</param>
    void CopyPlayers(string instanceKey, List<PlayerPresence> into);

    /// <summary>Sends one packet to one connection, with the same transport and encryption as <see cref="MulticastAsync"/>.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="packet">The packet; the caller keeps ownership and may return it to its pool afterwards.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the packet was sent.</returns>
    ValueTask SendAsync(IConnection connection, IPacket packet, CancellationToken cancellationToken = default);

    /// <summary>Sends one packet to every player of an instance. Best effort: a dead connection never fails the rest.</summary>
    /// <param name="instanceKey">Instance key (<see cref="MapInstance.Key"/>); the map id for a shared map.</param>
    /// <param name="packet">The packet; the caller keeps ownership and may return it to its pool afterwards.</param>
    /// <param name="except">A connection to skip, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when every send finished.</returns>
    ValueTask MulticastAsync(string instanceKey, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default);
}
