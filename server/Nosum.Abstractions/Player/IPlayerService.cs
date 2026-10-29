// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Player;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.Player;

/// <summary>
/// The player's place in the world: entering the world, walking on a map and changing maps. The server
/// owns positions and decides every map change; clients only claim positions.
/// </summary>
public interface IPlayerService
{
    /// <summary>
    /// Puts a connection in the world, or refuses. The map is the server's choice: where the player was last, else the starting map.
    /// </summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="accountId">The signed-in account behind the connection (from the server's own sign-in record, never from a packet).</param>
    /// <param name="mapId">Map key named by the client; must be a hosted map, but does not choose the map.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome code, the player id, the map and the spawn.</returns>
    ValueTask<JoinOutcome> JoinAsync(IConnection connection, Guid accountId, string mapId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a claimed position and applies it when legal. When the accepted position puts the player on an exit
    /// of the map (and they did not just arrive on it), also moves them to the exit's target map: the player
    /// leaves the old map's group, is told with a <c>MapChanged</c> push, enters the new map's group and the
    /// new place is saved.
    /// </summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="x">Claimed feet x.</param>
    /// <param name="y">Claimed feet y.</param>
    /// <param name="facing">Claimed facing, 0 to 3.</param>
    /// <param name="moving">Claimed walking flag.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Whether it was accepted and, when not, why and where the player is kept.</returns>
    ValueTask<MoveOutcome> MoveAsync(IConnection connection, float x, float y, byte facing, bool moving, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a connected player to the spawn of another instance on the server's own initiative: a visit to somebody's farm, or being sent
    /// out of it. The same steps as walking onto an exit (leave the old place, <c>MapChanged</c> push, enter the new one, save), but only when
    /// <see cref="Presence.IInstanceAccessPolicy"/> admits the player. A client never names an instance (docs/adr/ADR-019 D4).
    /// </summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="destination">The instance to enter; its map's spawn is the arrival point.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Whether the player arrived, and when not why.</returns>
    ValueTask<TravelStatus> TravelAsync(IConnection connection, MapInstance destination, CancellationToken cancellationToken = default);

    /// <summary>Writes the current map and position of every connected player to storage.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when every player's position is saved.</returns>
    ValueTask FlushPositionsAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}

/// <summary>How <see cref="IPlayerService.TravelAsync"/> ended.</summary>
public enum TravelStatus
{
    /// <summary>The player is now in the destination.</summary>
    Arrived = 0,

    /// <summary>The connection is on no map, or is already being moved to another.</summary>
    NotJoined = 1,

    /// <summary>The access policy does not admit the player, or the map is not hosted.</summary>
    NotAllowed = 2,
}

/// <summary>Outcome of <see cref="IPlayerService.JoinAsync"/>.</summary>
/// <param name="Status">Outcome code.</param>
/// <param name="PlayerId">Player id; zero unless the status is <see cref="JoinStatus.Ok"/>.</param>
/// <param name="SpawnX">Spawn feet x.</param>
/// <param name="SpawnY">Spawn feet y.</param>
/// <param name="MapId">The map the player was put on; empty unless the status is <see cref="JoinStatus.Ok"/>.</param>
/// <param name="Facing">Facing at the spawn, 0 down, 1 right, 2 up, 3 left.</param>
public readonly record struct JoinOutcome(JoinStatus Status, uint PlayerId, float SpawnX, float SpawnY, string MapId = "", byte Facing = 0);

/// <summary>How <see cref="IPlayerService.MoveAsync"/> ended.</summary>
public enum MoveStatus
{
    /// <summary>The claim was legal and applied.</summary>
    Accepted = 0,

    /// <summary>The claim was refused; the player is kept where they were.</summary>
    Refused = 1,

    /// <summary>The connection is on no map (or is already being moved to another), so there is nothing to move.</summary>
    NotJoined = 2,

    /// <summary>The claim was legal and put the player on an exit: they are now on another map and were told so.</summary>
    Warped = 3,
}

/// <summary>Outcome of <see cref="IPlayerService.MoveAsync"/>.</summary>
/// <param name="Status">Accepted, refused, not joined or warped.</param>
/// <param name="Reason">Why it was refused; meaningful only for <see cref="MoveStatus.Refused"/>.</param>
/// <param name="X">Feet x the server keeps (on the new map when warped).</param>
/// <param name="Y">Feet y the server keeps (on the new map when warped).</param>
public readonly record struct MoveOutcome(MoveStatus Status, MoveRefusal Reason, float X, float Y);
