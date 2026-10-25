// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using Nalix.Abstractions.Networking;
using Nosum.Gameplay.Movement;

namespace Nosum.Abstractions.Presence;

/// <summary>
/// One connected player standing on one map. The motion state is shared between the handler that
/// moves the player and the snapshot timer that reads it: hold <see cref="Gate"/> while touching it.
/// </summary>
public sealed class PlayerPresence
{
    private MotionState _motion;

    /// <summary>Initializes a presence record.</summary>
    /// <param name="playerId">Id unique among connected players.</param>
    /// <param name="mapId">Map the player stands on.</param>
    /// <param name="connection">The player's connection.</param>
    /// <param name="motion">Starting motion state.</param>
    public PlayerPresence(uint playerId, string mapId, IConnection connection, MotionState motion)
    {
        ArgumentException.ThrowIfNullOrEmpty(mapId);
        ArgumentNullException.ThrowIfNull(connection);

        this.PlayerId = playerId;
        this.MapId = mapId;
        this.Connection = connection;
        _motion = motion;
    }

    /// <summary>Gets the player id, unique among connected players.</summary>
    public uint PlayerId { get; }

    /// <summary>Gets the map the player stands on.</summary>
    public string MapId { get; }

    /// <summary>Gets the player's connection.</summary>
    public IConnection Connection { get; }

    /// <summary>Gets the lock that guards <see cref="Motion"/>.</summary>
    public Lock Gate { get; } = new();

    /// <summary>Gets the authoritative motion state; use only while holding <see cref="Gate"/>.</summary>
    public ref MotionState Motion => ref _motion;
}
