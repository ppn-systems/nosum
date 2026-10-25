// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Buildings;
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
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Visits;

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
    private readonly IChunkMap _chunks;
    private readonly ChunkStreamer _streamer;
    private readonly IMapCatalog _maps;
    private readonly IPlayerStateService _states;
    private readonly IInstanceWorld _world;
    private readonly IInstanceAccessPolicy _access;
    private readonly ICoopMembership _coops;
    private readonly TimeProvider _time;
    private readonly IGroundItems? _groundItems;
    private readonly IBuildingDirectory? _buildings;

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands on which map.</param>
    /// <param name="chunks">Who is interested in which chunk: the snapshot of a player carries only the players in their interest set (docs/adr/ADR-022).</param>
    /// <param name="streamer">Sends chunk state, spawns and despawns as the interest sets change.</param>
    /// <param name="maps">The hosted maps.</param>
    /// <param name="states">Saved player positions.</param>
    /// <param name="world">The per-instance layer (placed furniture) players collide with.</param>
    /// <param name="access">Who may enter which instance.</param>
    /// <param name="coops">Which co-op a player belongs to: the owner of the co-op land they may enter.</param>
    /// <param name="time">Clock for movement budgets and snapshot timestamps.</param>
    /// <param name="snapshotInterval">Time between map snapshots.</param>
    /// <param name="groundItems">Told after every accepted move so items within reach are picked up (docs/adr/ADR-021 D6); <see langword="null"/> for none.</param>
    /// <param name="buildings">The farm buildings: walking onto a building's door enters its interior, and leaving the interior returns to the door (docs/adr/ADR-028 D2); <see langword="null"/> for none.</param>
    public PlayerService(IMapPresence presence, IChunkMap chunks, ChunkStreamer streamer, IMapCatalog maps, IPlayerStateService states, IInstanceWorld world, IInstanceAccessPolicy access, ICoopMembership coops, TimeProvider time, TimeSpan snapshotInterval, IGroundItems? groundItems = null, IBuildingDirectory? buildings = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(streamer);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(coops);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(snapshotInterval, TimeSpan.Zero);

        _presence = presence;
        _chunks = chunks;
        _streamer = streamer;
        _maps = maps;
        _states = states;
        _world = world;
        _access = access;
        _coops = coops;
        _time = time;
        _groundItems = groundItems;
        _buildings = buildings;
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
        this.SubscribeToChunks(player, map);

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
        BuildingInfo? door = null;
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
                // No exit of the map's own: standing on a farm building's door goes into its interior (docs/adr/ADR-028 D2).
                if (!this.TryFindDoor(player, map, out door))
                {
                    ArmWarps(player, map);
                    return new MoveOutcome(MoveStatus.Accepted, 0, x, y);
                }

                warp = DoorWarp(door);
            }

            if (!player.WarpArmed)
            {
                return new MoveOutcome(MoveStatus.Accepted, 0, x, y);
            }

            player.InTransit = true;
        }

        return await this.WarpAsync(connection, player, warp, door?.Interior).ConfigureAwait(false);
    }

    // The building whose door tile the player's feet are on; the tile is computed from the position the server keeps.
    private bool TryFindDoor(PlayerPresence player, MapDefinition map, [NotNullWhen(true)] out BuildingInfo? door)
    {
        door = null;
        return _buildings is not null
            && player.Instance.IsPrivate
            && _buildings.TryFindDoor(player.Instance, WarpRule.TileX(player.Motion.X, map.Grid.TileSize), WarpRule.TileY(player.Motion.Y, map.Grid.TileSize), out door);
    }

    // The exit a door is: from its tile to the interior's arrival tile.
    private static MapWarp DoorWarp(BuildingInfo building)
    {
        _ = BuildingCatalog.TryGet(building.Kind, out BuildingDefinition? definition);
        (int X, int Y) doorTile = building.Doors.Door;
        return new MapWarp(doorTile.X, doorTile.Y, building.Interior.MapId, definition!.ArrivalX, definition.ArrivalY);
    }

    // The saved place when its map is hosted and the place is still legal (the map may have changed since);
    // a hosted map with a bad place gets that map's spawn; no saved place, or an unhosted map, gets the starting map's spawn.
    // A player saved on co-op land who no longer belongs to a co-op starts on the starting map instead.
    private async ValueTask<(MapDefinition Map, MotionState Start, MapInstance Instance)> StartingPlaceAsync(Guid accountId, CancellationToken cancellationToken)
    {
        double now = this.NowSeconds();
        PlayerStateRow? saved = await _states.LoadAsync(accountId, cancellationToken).ConfigureAwait(false);
        // The inside of a farm building belongs to the building, not to the player: a player who left the game in there starts on the farm.
        if (saved is { } row && !(GameMaps.TryGet(row.MapName, out MapSpec? savedSpec) && savedSpec.IsInterior) && _maps.TryGet(row.MapName, out MapDefinition? savedMap)
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
    private async ValueTask<MoveOutcome> WarpAsync(IConnection connection, PlayerPresence from, MapWarp warp, MapInstance? interior = null)
    {
        MapInstance targetInstance = default;
        bool allowed = false;
        MapDefinition? found = null;
        try
        {
            if (interior is { } inner && _maps.TryGet(inner.MapId, out found))
            {
                // A farm building's door: the interior of that building, if the access policy lets this player in (finished, and theirs or open to them).
                targetInstance = inner;
                allowed = await _access.CanEnterAsync(inner, from.AccountId, CancellationToken.None).ConfigureAwait(false);
            }
            else if (GameMaps.TryGet(from.MapId, out MapSpec? fromSpec) && fromSpec.IsInterior && string.Equals(warp.TargetMap, GameMaps.Farm, StringComparison.Ordinal))
            {
                // The way out of a building leads to the land it stands on, in front of its door, whatever the map file says.
                (found, targetInstance, warp, allowed) = await this.ExitOfBuildingAsync(from, warp).ConfigureAwait(false);
            }
            else if (_maps.TryGet(warp.TargetMap, out found))
            {
                if (found.IsCoop)
                {
                    // Co-op land is the player's own co-op's copy, found from their membership; a player with no co-op is refused at the gate.
                    if (await this.InstanceOfAsync(found, from.AccountId, CancellationToken.None).ConfigureAwait(false) is { } coopLand)
                    {
                        targetInstance = coopLand;
                        allowed = true;
                    }
                }
                else
                {
                    targetInstance = InstanceFor(found, from);
                    allowed = await _access.CanEnterAsync(targetInstance, from.AccountId, CancellationToken.None).ConfigureAwait(false);
                }
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

        if (!allowed || found is null)
        {
            // A refused player stays put, and the exit is disarmed until they step off it so they do not ask again every move.
            lock (from.Gate)
            {
                from.InTransit = false;
                from.WarpArmed = false;
                return new MoveOutcome(MoveStatus.Accepted, 0, (float)from.Motion.X, (float)from.Motion.Y);
            }
        }

        MapDefinition target = found;

        (double x, double y, int facing) = WarpRule.Arrival(target.Grid, warp.TargetX, warp.TargetY);
        await this.RelocateAsync(connection, from, target, targetInstance, x, y, facing).ConfigureAwait(false);
        return new MoveOutcome(MoveStatus.Warped, 0, (float)x, (float)y);
    }

    // Where leaving a building leads: its land (the player's farm, a visited farm or co-op land) at the tile in front of the door, or the Town when the player may no longer be on that land.
    private async ValueTask<(MapDefinition? Map, MapInstance Instance, MapWarp Warp, bool Allowed)> ExitOfBuildingAsync(PlayerPresence from, MapWarp warp)
    {
        BuildingInfo? building = _buildings is null ? null : await _buildings.FindAsync(from.Instance.OwnerId, CancellationToken.None).ConfigureAwait(false);
        if (building is not null && _maps.TryGet(building.Land.MapId, out MapDefinition? land))
        {
            bool may;
            if (building.Land.IsCoopLand)
            {
                CoopMembershipInfo? member = await _coops.FindAsync(from.AccountId, CancellationToken.None).ConfigureAwait(false);
                may = member is not null && member.CoopId == building.Land.OwnerId;
            }
            else
            {
                may = await _access.CanEnterAsync(building.Land, from.AccountId, CancellationToken.None).ConfigureAwait(false);
            }

            if (may)
            {
                (int X, int Y) front = building.Doors.Front;
                return (land, building.Land, warp with { TargetMap = land.Id, TargetX = front.X, TargetY = front.Y }, true);
            }
        }

        // The land is closed to them now (a visit was revoked, a membership ended): out to the Town, like the visitors who were on the land itself.
        _ = GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
        _ = _maps.TryGet(GameMaps.Town, out MapDefinition? townMap);
        return (townMap, MapInstance.Shared(GameMaps.Town), warp with { TargetMap = GameMaps.Town, TargetX = town!.SpawnTileX, TargetY = town.SpawnTileY }, townMap is not null);
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
        this.SubscribeToChunks(arrived, target);
        this.EnsureSnapshotLoop();

        // A membership that ended while the player was on the way in must not leave them on the land.
        if (target.IsCoop && await _coops.FindAsync(from.AccountId, CancellationToken.None).ConfigureAwait(false) is null)
        {
            await this.EvictPresenceAsync(arrived).ConfigureAwait(false);
            return;
        }

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

    /// <inheritdoc/>
    public async ValueTask EvictAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        List<string> keys = [];
        _presence.CopyActiveMapIds(keys);
        List<PlayerPresence> players = [];
        foreach (string key in keys)
        {
            int at = key.IndexOf('@', StringComparison.Ordinal);
            if (at > 0 && GameMaps.TryGet(key[..at], out MapSpec? spec) && (spec.IsCoop || await this.IsInteriorOnCoopLandAsync(spec, key[(at + 1)..]).ConfigureAwait(false)))
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

    // Whether an instance key names the inside of a building that stands on co-op land: members leaving the co-op must leave it too.
    private async ValueTask<bool> IsInteriorOnCoopLandAsync(MapSpec spec, string ownerPart)
    {
        if (!spec.IsInterior || _buildings is null || !Guid.TryParseExact(ownerPart, "N", out Guid buildingId))
        {
            return false;
        }

        BuildingInfo? building = await _buildings.FindAsync(buildingId, CancellationToken.None).ConfigureAwait(false);
        return building is not null && building.Land.IsCoopLand;
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

        // The players who can see the leaver hear at once; anyone the notice misses loses them from the next snapshot (docs/adr/ADR-022 D3).
        ChunkCoord chunk = player.Viewer?.Chunk ?? default;
        if (player.Viewer is null && _maps.TryGet(player.MapId, out MapDefinition? map))
        {
            lock (player.Gate)
            {
                chunk = ChunkCoord.OfPixels(player.Motion.X, player.Motion.Y, map.Grid.TileSize);
            }
        }

        await _streamer.PublishLeftAsync(player, chunk).ConfigureAwait(false);
    }

    // Puts a player who just entered an instance into the chunk index with the 3 x 3 block around their chunk; their chunk state is sent by the next snapshot tick.
    private void SubscribeToChunks(PlayerPresence player, MapDefinition map)
    {
        ChunkBounds bounds = ChunkBounds.ForTiles(map.Grid.Width, map.Grid.Height);
        ChunkCoord chunk;
        lock (player.Gate)
        {
            chunk = bounds.Clamp(ChunkCoord.OfPixels(player.Motion.X, player.Motion.Y, map.Grid.TileSize));
        }

        _chunks.Subscribe(player, chunk, bounds);
    }
}
