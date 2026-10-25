// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Core;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;

namespace Nosum.Infrastructure.Players;

/// <summary>
/// Places players in the world (on the map they were last on, if saved; farm and farmhouse are each player's own instance, docs/adr/ADR-015), validates their movement with
/// <see cref="MoveValidator"/>, changes their map when they walk onto an exit (<see cref="WarpRule"/>) and
/// saves their place. Co-op land (docs/adr/ADR-020) is entered at the co-op the player belongs to and nobody else's; ending a membership moves the player out. Pushes each map's state to its players while anyone is on it
/// (see <c>PlayerService.Snapshots.cs</c>).
/// </summary>
public sealed partial class PlayerService : IPlayerService, ICoopLandEviction, IAsyncDisposable
{
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IPlayerStateService _states;
    private readonly IInstanceWorld _world;
    private readonly IInstanceAccessPolicy _access;
    private readonly ICoopMembership _coops;
    private readonly TimeProvider _time;
    private readonly IGroundItems? _groundItems;

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands on which map.</param>
    /// <param name="maps">The hosted maps.</param>
    /// <param name="states">Saved player positions.</param>
    /// <param name="world">The per-instance layer (placed furniture) players collide with.</param>
    /// <param name="access">Who may enter which instance.</param>
    /// <param name="coops">Which co-op a player belongs to: the owner of the co-op land they may enter.</param>
    /// <param name="time">Clock for movement budgets and snapshot timestamps.</param>
    /// <param name="snapshotInterval">Time between map snapshots.</param>
    /// <param name="groundItems">Told after every accepted move so items within reach are picked up (docs/adr/ADR-021 D6); <see langword="null"/> for none.</param>
    public PlayerService(IMapPresence presence, IMapCatalog maps, IPlayerStateService states, IInstanceWorld world, IInstanceAccessPolicy access, ICoopMembership coops, TimeProvider time, TimeSpan snapshotInterval, IGroundItems? groundItems = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(coops);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(snapshotInterval, TimeSpan.Zero);

        _presence = presence;
        _maps = maps;
        _states = states;
        _world = world;
        _access = access;
        _coops = coops;
        _time = time;
        _groundItems = groundItems;
        _snapshotInterval = snapshotInterval;
        _presence.PlayerLeft += this.OnPlayerLeftAsync;
    }

    /// <inheritdoc/>
    public async ValueTask<JoinOutcome> JoinAsync(IConnection connection, Guid accountId, string mapId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // The id only has to name a hosted map; which map the player enters is the server's decision.
        if (!_maps.TryGet(mapId, out _))
        {
            return new JoinOutcome(JoinStatus.UnknownMap, 0, 0, 0);
        }

        (MapDefinition map, MotionState start, MapInstance instance) = await this.StartingPlaceAsync(accountId, cancellationToken).ConfigureAwait(false);
        await _world.PrepareAsync(instance, cancellationToken).ConfigureAwait(false);
        PlayerPresence player = await _presence.JoinAsync(connection, accountId, instance, start, cancellationToken: cancellationToken).ConfigureAwait(false);
        ArmWarps(player, map);

        this.EnsureSnapshotLoop();
        return new JoinOutcome(JoinStatus.Ok, player.PlayerId, (float)start.X, (float)start.Y, map.Id, (byte)start.Facing);
    }

    /// <inheritdoc/>
    public async ValueTask<MoveOutcome> MoveAsync(IConnection connection, float x, float y, byte facing, bool moving, CancellationToken cancellationToken = default)
    {
        MoveOutcome outcome = await this.ValidateMoveAsync(connection, x, y, facing, moving).ConfigureAwait(false);
        if (outcome.Status == MoveStatus.Accepted && _groundItems is not null && _presence.TryGet(connection, out PlayerPresence? player))
        {
            // Items within reach are picked up off the movement path: the move is answered without waiting for the database.
            _ = _groundItems.OnPlayerMovedAsync(player);
        }

        return outcome;
    }

    private async ValueTask<MoveOutcome> ValidateMoveAsync(IConnection connection, float x, float y, byte facing, bool moving)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player) || !_maps.TryGet(player.MapId, out MapDefinition? map))
        {
            return new MoveOutcome(MoveStatus.NotJoined, 0, 0, 0);
        }

        double now = this.NowSeconds();
        MapWarp warp;
        lock (player.Gate)
        {
            if (player.InTransit)
            {
                return new MoveOutcome(MoveStatus.NotJoined, 0, 0, 0);
            }

            MoveVerdict verdict = MoveValidator.Validate(ref player.Motion, x, y, facing, moving, now, _world.GridFor(player.Instance, map));
            if (verdict != MoveVerdict.Accepted)
            {
                MoveRefusal reason = verdict switch
                {
                    MoveVerdict.TooFast => MoveRefusal.TooFast,
                    MoveVerdict.OutOfMap => MoveRefusal.OutOfMap,
                    MoveVerdict.Blocked => MoveRefusal.Blocked,
                    _ => MoveRefusal.Invalid,
                };
                return new MoveOutcome(MoveStatus.Refused, reason, (float)player.Motion.X, (float)player.Motion.Y);
            }

            // The exit is looked up from the position the server keeps, never from what the client says about doors.
            if (!WarpRule.TryFind(map.Warps, map.Grid, player.Motion.X, player.Motion.Y, player.Motion.Facing, player.Motion.Moving, out warp))
            {
                ArmWarps(player, map);
                return new MoveOutcome(MoveStatus.Accepted, 0, x, y);
            }

            if (!player.WarpArmed)
            {
                return new MoveOutcome(MoveStatus.Accepted, 0, x, y);
            }

            player.InTransit = true;
        }

        return await this.WarpAsync(connection, player, warp).ConfigureAwait(false);
    }

    // The saved place when its map is hosted and the place is still legal (the map may have changed since);
    // a hosted map with a bad place gets that map's spawn; no saved place, or an unhosted map, gets the starting map's spawn.
    // A player saved on co-op land who no longer belongs to a co-op starts on the starting map instead.
    private async ValueTask<(MapDefinition Map, MotionState Start, MapInstance Instance)> StartingPlaceAsync(Guid accountId, CancellationToken cancellationToken)
    {
        double now = this.NowSeconds();
        PlayerStateRow? saved = await _states.LoadAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (saved is { } row && _maps.TryGet(row.MapName, out MapDefinition? savedMap)
            && await this.InstanceOfAsync(savedMap, accountId, cancellationToken).ConfigureAwait(false) is { } savedInstance)
        {
            return (savedMap, IsLegalPlace(savedMap, row)
                ? new MotionState(row.X, row.Y, now, (int)row.Facing)
                : new MotionState(savedMap.SpawnX, savedMap.SpawnY, now), savedInstance);
        }

        if (!_maps.TryGet(GameMaps.DefaultId, out MapDefinition? start))
        {
            throw new InvalidOperationException("The starting map '" + GameMaps.DefaultId + "' is not hosted.");
        }

        return (start, new MotionState(start.SpawnX, start.SpawnY, now), InstanceOf(start, accountId));
    }

    // The instance a player enters, or null when they may not enter the map: co-op land needs a co-op, and is that co-op's own copy.
    private async ValueTask<MapInstance?> InstanceOfAsync(MapDefinition map, Guid accountId, CancellationToken cancellationToken)
    {
        if (!map.IsCoop)
        {
            return InstanceOf(map, accountId);
        }

        CoopMembershipInfo? member = await _coops.FindAsync(accountId, cancellationToken).ConfigureAwait(false);
        return member is null ? null : MapInstance.OfCoop(map.Id, member.CoopId);
    }

    // A private map is entered at the entering player's own instance; nobody can name another one (visiting goes through IInstanceAccessPolicy).
    private static MapInstance InstanceOf(MapDefinition map, Guid playerId) => map.IsPrivate ? new MapInstance(map.Id, playerId) : MapInstance.Shared(map.Id);

    private static bool IsLegalPlace(MapDefinition map, PlayerStateRow row)
        => double.IsFinite(row.X)
            && double.IsFinite(row.Y)
            && row.X >= 0
            && row.Y >= 0
            && row.X < map.Grid.Width * map.Grid.TileSize
            && row.Y < map.Grid.Height * map.Grid.TileSize
            && !MoveValidator.FootBlocked(map.Grid, row.X, row.Y);

    // A player may use an exit only after standing off every exit tile since they arrived (or they would bounce straight back).
    private static void ArmWarps(PlayerPresence player, MapDefinition map)
    {
        lock (player.Gate)
        {
            if (!player.WarpArmed && !WarpRule.IsOnExitTile(map.Warps, map.Grid, player.Motion.X, player.Motion.Y))
            {
                player.WarpArmed = true;
            }
        }
    }

    // Leaves the old map (its players are told, the old place is saved), tells the player where they now are
    // while they are on no map (so no snapshot of the new map can reach them first), then enters the new map and saves the new place.
    [SuppressMessage("Design", "CA1031", Justification = "Saving is best effort; the player is already on the new map.")]
    private async ValueTask<MoveOutcome> WarpAsync(IConnection connection, PlayerPresence from, MapWarp warp)
    {
        MapInstance? entered = null;
        if (_maps.TryGet(warp.TargetMap, out MapDefinition? found))
        {
            // Co-op land is the player's own co-op's copy, found from their membership, so it needs no allow-list check; the other maps go through the policy.
            entered = await this.InstanceOfAsync(found, from.AccountId, CancellationToken.None).ConfigureAwait(false);
            if (entered is { } candidate && !found.IsCoop && !_access.CanEnter(candidate, from.AccountId))
            {
                entered = null;
            }
        }

        if (found is null || entered is not { } targetInstance)
        {
            // Exits to maps the catalog does not host are dropped when it is built; a player with no co-op is refused at the gate of co-op land.
            // Either way they stay put, and the exit is disarmed until they step off it so a refused player does not ask again every move.
            lock (from.Gate)
            {
                from.InTransit = false;
                from.WarpArmed = false;
                return new MoveOutcome(MoveStatus.Accepted, 0, (float)from.Motion.X, (float)from.Motion.Y);
            }
        }

        MapDefinition target = found;

        (double x, double y, int facing) = WarpRule.Arrival(target.Grid, warp.TargetX, warp.TargetY);

        await _world.PrepareAsync(targetInstance, CancellationToken.None).ConfigureAwait(false);
        await _presence.LeaveAsync(connection, CancellationToken.None).ConfigureAwait(false);

        using (PacketScope<MapChanged> lease = PacketFactory<MapChanged>.Acquire())
        {
            lease.Value.PlayerId = from.PlayerId;
            lease.Value.MapId = target.Id;
            lease.Value.X = (float)x;
            lease.Value.Y = (float)y;
            lease.Value.Facing = (byte)facing;
            await _presence.SendAsync(connection, lease.Value, CancellationToken.None).ConfigureAwait(false);
        }

        PlayerPresence arrived = await _presence.JoinAsync(connection, from.AccountId, targetInstance, new MotionState(x, y, this.NowSeconds(), facing), from.PlayerId, CancellationToken.None).ConfigureAwait(false);
        ArmWarps(arrived, target);
        this.EnsureSnapshotLoop();

        // A membership that ended while the player was on the way in must not leave them on the land.
        if (target.IsCoop && await _coops.FindAsync(from.AccountId, CancellationToken.None).ConfigureAwait(false) is null)
        {
            await this.EvictPresenceAsync(arrived).ConfigureAwait(false);
            return new MoveOutcome(MoveStatus.Warped, 0, (float)x, (float)y);
        }

        try
        {
            await _states.SaveAsync(from.AccountId, new PlayerStateRow(target.Id, x, y, (Facing)facing), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The leave above saved the old place; the next leave saves the new one.
        }

        return new MoveOutcome(MoveStatus.Warped, 0, (float)x, (float)y);
    }

    /// <inheritdoc/>
    public async ValueTask EvictAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        List<string> keys = [];
        _presence.CopyActiveMapIds(keys);
        List<PlayerPresence> players = [];
        foreach (string key in keys)
        {
            int at = key.IndexOf('@', StringComparison.Ordinal);
            if (at > 0 && GameMaps.TryGet(key[..at], out MapSpec? spec) && spec.IsCoop)
            {
                _presence.CopyPlayers(key, players);
            }
        }

        foreach (PlayerPresence player in players)
        {
            if (player.AccountId == accountId)
            {
                await this.EvictPresenceAsync(player).ConfigureAwait(false);
            }
        }
    }

    // Sends the player back to Town; a player already mid-warp is left to the membership re-check after that warp's arrival.
    private async ValueTask EvictPresenceAsync(PlayerPresence player)
    {
        lock (player.Gate)
        {
            if (player.InTransit)
            {
                return;
            }

            player.InTransit = true;
        }

        _ = await this.WarpAsync(player.Connection, player, new MapWarp(CoopLand.LandGate.X, CoopLand.LandGate.Y, GameMaps.Town, CoopLand.TownArrival.X, CoopLand.TownArrival.Y)).ConfigureAwait(false);
    }

    private double NowSeconds() => _time.GetTimestamp() / (double)_time.TimestampFrequency;

    [SuppressMessage("Design", "CA1031", Justification = "Saving is best effort on the disconnect path; the leave notice to the map must still go out.")]
    private async ValueTask OnPlayerLeftAsync(PlayerPresence player)
    {
        try
        {
            PlayerStateRow state;
            lock (player.Gate)
            {
                state = new PlayerStateRow(player.MapId, player.Motion.X, player.Motion.Y, (Facing)player.Motion.Facing);
            }

            await _states.SaveAsync(player.AccountId, state, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The player keeps their previously saved place.
        }

        using PacketScope<PlayerLeft> lease = PacketFactory<PlayerLeft>.Acquire();
        lease.Value.PlayerId = player.PlayerId;
        await _presence.MulticastAsync(player.Key, lease.Value).ConfigureAwait(false);
    }
}
