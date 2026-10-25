// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using Nalix.Abstractions.Networking;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;

namespace Nosum.Abstractions.Presence;

/// <summary>
/// One connected player standing in one map instance. The motion state is shared between the handler that
/// moves the player and the snapshot timer that reads it: hold <see cref="Gate"/> while touching it.
/// </summary>
public sealed class PlayerPresence
{
    private MotionState _motion;

    /// <summary>Initializes a presence record.</summary>
    /// <param name="playerId">Id unique among connected players.</param>
    /// <param name="accountId">The signed-in account behind the connection.</param>
    /// <param name="instance">The place the player stands in.</param>
    /// <param name="connection">The player's connection.</param>
    /// <param name="motion">Starting motion state.</param>
    public PlayerPresence(uint playerId, Guid accountId, MapInstance instance, IConnection connection, MotionState motion)
    {
        ArgumentException.ThrowIfNullOrEmpty(instance.MapId);
        ArgumentNullException.ThrowIfNull(connection);

        this.PlayerId = playerId;
        this.AccountId = accountId;
        this.Instance = instance;
        this.Connection = connection;
        _motion = motion;
    }

    /// <summary>Gets the player id, unique among connected players.</summary>
    public uint PlayerId { get; }

    /// <summary>Gets the account id of the signed-in player; the id persisted data hangs off.</summary>
    public Guid AccountId { get; }

    /// <summary>Gets the place the player stands in: a shared map, or their own copy of a private one.</summary>
    public MapInstance Instance { get; }

    /// <summary>Gets the id of the map the player stands on (the same for every copy of a private map).</summary>
    public string MapId => this.Instance.MapId;

    /// <summary>
    /// Gets the key of the place the player stands in (<see cref="MapInstance.Key"/>). Use it, not <see cref="MapId"/>, to find or send to
    /// "the players on the same map": for a private farm or farmhouse only the owner shares it.
    /// </summary>
    public string Key => this.Instance.Key;

    /// <summary>Gets the player's connection.</summary>
    public IConnection Connection { get; }

    /// <summary>Gets the lock that guards <see cref="Motion"/>.</summary>
    public Lock Gate { get; } = new();

    /// <summary>Gets the authoritative motion state; use only while holding <see cref="Gate"/>.</summary>
    public ref MotionState Motion => ref _motion;

    /// <summary>
    /// Gets or sets whether the player may take an exit: false from arriving on an exit tile until they have stood
    /// off every exit tile, so a player put on a door is not sent straight back. Use only while holding <see cref="Gate"/>.
    /// </summary>
    public bool WarpArmed { get; set; }

    /// <summary>Gets or sets whether the server is already moving the player to another map; later moves are ignored. Use only while holding <see cref="Gate"/>.</summary>
    public bool InTransit { get; set; }
}
