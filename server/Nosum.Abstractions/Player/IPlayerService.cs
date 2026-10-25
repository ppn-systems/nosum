// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Player;

namespace Nosum.Abstractions.Player;

/// <summary>
/// The player's place in the world: entering a map and walking on it. The server owns positions;
/// clients only claim them.
/// </summary>
public interface IPlayerService
{
    /// <summary>Puts a connection on a map, or refuses.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="mapId">Map key named by the client.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome code, the player id and the spawn.</returns>
    ValueTask<JoinOutcome> JoinAsync(IConnection connection, string mapId, CancellationToken cancellationToken = default);

    /// <summary>Checks a claimed position and applies it when legal.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="x">Claimed feet x.</param>
    /// <param name="y">Claimed feet y.</param>
    /// <param name="facing">Claimed facing, 0 to 3.</param>
    /// <param name="moving">Claimed walking flag.</param>
    /// <returns>Whether it was accepted and, when not, why and where the player is kept.</returns>
    MoveOutcome Move(IConnection connection, float x, float y, byte facing, bool moving);
}

/// <summary>Outcome of <see cref="IPlayerService.JoinAsync"/>.</summary>
/// <param name="Status">Outcome code.</param>
/// <param name="PlayerId">Player id; zero unless the status is <see cref="JoinStatus.Ok"/>.</param>
/// <param name="SpawnX">Spawn feet x.</param>
/// <param name="SpawnY">Spawn feet y.</param>
public readonly record struct JoinOutcome(JoinStatus Status, uint PlayerId, float SpawnX, float SpawnY);

/// <summary>How <see cref="IPlayerService.Move"/> ended.</summary>
public enum MoveStatus
{
    /// <summary>The claim was legal and applied.</summary>
    Accepted = 0,

    /// <summary>The claim was refused; the player is kept where they were.</summary>
    Refused = 1,

    /// <summary>The connection is on no map, so there is nothing to move.</summary>
    NotJoined = 2,
}

/// <summary>Outcome of <see cref="IPlayerService.Move"/>.</summary>
/// <param name="Status">Accepted, refused or not joined.</param>
/// <param name="Reason">Why it was refused; meaningful only for <see cref="MoveStatus.Refused"/>.</param>
/// <param name="X">Feet x the server keeps.</param>
/// <param name="Y">Feet y the server keeps.</param>
public readonly record struct MoveOutcome(MoveStatus Status, MoveRefusal Reason, float X, float Y);
