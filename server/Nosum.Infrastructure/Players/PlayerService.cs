// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Visits;

namespace Nosum.Infrastructure.Players;

/// <summary>
/// Places players in the world (on the map they were last on, if saved; farm and farmhouse are each player's own instance, docs/adr/ADR-015), validates their movement with
/// <see cref="MoveValidator"/>, changes their map when they walk onto an exit (<see cref="WarpRule"/>) and
/// saves their place. Pushes each map's state to its players while anyone is on it
/// (see <c>PlayerService.Snapshots.cs</c>).
/// </summary>
public sealed partial class PlayerService : IPlayerService, IAsyncDisposable
{
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IPlayerStateService _states;
    private readonly IInstanceWorld _world;
    private readonly IInstanceAccessPolicy _access;
    private readonly TimeProvider _time;

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands on which map.</param>
    /// <param name="maps">The hosted maps.</param>
    /// <param name="states">Saved player positions.</param>
    /// <param name="world">The per-instance layer (placed furniture) players collide with.</param>
    /// <param name="access">Who may enter which instance.</param>
    /// <param name="time">Clock for movement budgets and snapshot timestamps.</param>
    /// <param name="snapshotInterval">Time between map snapshots.</param>
    public PlayerService(IMapPresence presence, IMapCatalog maps, IPlayerStateService states, IInstanceWorld world, IInstanceAccessPolicy access, TimeProvider time, TimeSpan snapshotInterval)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(snapshotInterval, TimeSpan.Zero);

        _presence = presence;
        _maps = maps;
        _states = states;
        _world = world;
        _access = access;
        _time = time;
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

        (MapDefinition map, MotionState start) = await this.StartingPlaceAsync(accountId, cancellationToken).ConfigureAwait(false);
        MapInstance instance = InstanceOf(map, accountId);
        await _world.PrepareAsync(instance, cancellationToken).ConfigureAwait(false);
        PlayerPresence player = await _presence.JoinAsync(connection, accountId, instance, start, cancellationToken: cancellationToken).ConfigureAwait(false);
        ArmWarps(player, map);

        this.EnsureSnapshotLoop();
        return new JoinOutcome(JoinStatus.Ok, player.PlayerId, (float)start.X, (float)start.Y, map.Id, (byte)start.Facing);
    }

    /// <inheritdoc/>
    public async ValueTask<MoveOutcome> MoveAsync(IConnection connection, float x, float y, byte facing, bool moving, CancellationToken cancellationToken = default)
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
    private async ValueTask<(MapDefinition Map, MotionState Start)> StartingPlaceAsync(Guid accountId, CancellationToken cancellationToken)
    {
        double now = this.NowSeconds();
        PlayerStateRow? saved = await _states.LoadAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (saved is { } row && _maps.TryGet(row.MapName, out MapDefinition? savedMap))
        {
            return (savedMap, IsLegalPlace(savedMap, row)
                ? new MotionState(row.X, row.Y, now, (int)row.Facing)
                : new MotionState(savedMap.SpawnX, savedMap.SpawnY, now));
        }

        if (!_maps.TryGet(GameMaps.DefaultId, out MapDefinition? start))
        {
            throw new InvalidOperationException("The starting map '" + GameMaps.DefaultId + "' is not hosted.");
        }

        return (start, new MotionState(start.SpawnX, start.SpawnY, now));
    }

    // A join enters the player's own copy of a private map; nobody can name another one (visiting goes through TravelAsync and IInstanceAccessPolicy).
    private static MapInstance InstanceOf(MapDefinition map, Guid playerId) => map.IsPrivate ? new MapInstance(map.Id, playerId) : MapInstance.Shared(map.Id);

    // A door inside somebody else's farm leads to that owner's copy of the next private map (the farmhouse), so a visitor can walk in and out;
    // from a public map a door leads to the player's own copy. The access policy still decides whether the player may enter it.
    private static MapInstance InstanceFor(MapDefinition target, PlayerPresence from)
        => !target.IsPrivate ? MapInstance.Shared(target.Id) : new MapInstance(target.Id, from.Instance.IsPrivate ? from.Instance.OwnerId : from.AccountId);

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
    private async ValueTask<MoveOutcome> WarpAsync(IConnection connection, PlayerPresence from, MapWarp warp)
    {
        MapInstance targetInstance = default;
        bool allowed = false;
        MapDefinition? target = null;
        try
        {
            // Exits to maps the catalog does not host are dropped when it is built; stay put if one slipped through.
            if (_maps.TryGet(warp.TargetMap, out target))
            {
                targetInstance = InstanceFor(target, from);
                allowed = await _access.CanEnterAsync(targetInstance, from.AccountId, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            lock (from.Gate)
            {
                from.InTransit = false;
            }

            throw;
        }

        if (!allowed || target is null)
        {
            lock (from.Gate)
            {
                from.InTransit = false;
                return new MoveOutcome(MoveStatus.Accepted, 0, (float)from.Motion.X, (float)from.Motion.Y);
            }
        }

        (double x, double y, int facing) = WarpRule.Arrival(target.Grid, warp.TargetX, warp.TargetY);
        await this.RelocateAsync(connection, from, target, targetInstance, x, y, facing).ConfigureAwait(false);
        return new MoveOutcome(MoveStatus.Warped, 0, (float)x, (float)y);
    }

    /// <inheritdoc/>
    public async ValueTask<TravelStatus> TravelAsync(IConnection connection, MapInstance destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? from))
        {
            return TravelStatus.NotJoined;
        }

        // The instance must be what the map says it is: an owner for a private map, none for a shared one.
        if (!_maps.TryGet(destination.MapId, out MapDefinition? target) || destination.IsPrivate != target.IsPrivate
            || !await _access.CanEnterAsync(destination, from.AccountId, cancellationToken).ConfigureAwait(false))
        {
            return TravelStatus.NotAllowed;
        }

        lock (from.Gate)
        {
            if (from.InTransit)
            {
                return TravelStatus.NotJoined;
            }

            from.InTransit = true;
        }

        await this.RelocateAsync(connection, from, target, destination, target.SpawnX, target.SpawnY, (int)Facing.Down).ConfigureAwait(false);
        return TravelStatus.Arrived;
    }

    // The shared move between instances: used by walking onto an exit and by a visit. The caller has set InTransit and checked access.
    [SuppressMessage("Design", "CA1031", Justification = "Saving is best effort; the player is already on the new map.")]
    private async ValueTask RelocateAsync(IConnection connection, PlayerPresence from, MapDefinition target, MapInstance targetInstance, double x, double y, int facing)
    {
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

        try
        {
            await _states.SaveAsync(from.AccountId, new PlayerStateRow(target.Id, x, y, (Facing)facing), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The leave above saved the old place; the next leave saves the new one.
        }

        // The owner may have taken the permission away while the player was on the way: the revoke looks at the players already inside, so ask once more now that this one is.
        // Whichever of the two happens last sees the other, so nobody stays in a place they may not be in (docs/adr/ADR-019 D5).
        if (VisitRules.IsVisitor(targetInstance, from.AccountId) && !await _access.CanEnterAsync(targetInstance, from.AccountId, CancellationToken.None).ConfigureAwait(false))
        {
            _ = await this.TravelAsync(connection, MapInstance.Shared(GameMaps.Town), CancellationToken.None).ConfigureAwait(false);
        }
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
