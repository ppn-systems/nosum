// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Mines;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Mine;
using Nosum.Domain.Mines;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Shops;
using System.Collections.Generic;

namespace Nosum.Infrastructure.Mines;

/// <summary>
/// <see cref="IMineService"/> (docs/adr/ADR-026 D3): the elevator, the ladders and the deepest floor. A floor is the map <c>mine&lt;N&gt;</c> owned by one player, so nobody
/// else's presence, nodes or drops ever mix with it (the entry rule in <c>IInstanceAccessPolicy</c> keeps other players out). Its nodes are built when the
/// player enters (<c>ResourceNodeService</c>); this service forgets them, and the drops on the floor, when the last player leaves, and writes the deepest floor
/// when a player arrives on a deeper one.
/// </summary>
public sealed class MineService : IMineService, IDisposable
{
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IPlayerService _players;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IResourceNodeService _nodes;
    private readonly IGroundItems _groundItems;

    /// <summary>Initializes the service and starts watching arrivals and departures.</summary>
    /// <param name="presence">Who stands where.</param>
    /// <param name="maps">The hosted maps.</param>
    /// <param name="players">Moves a player between maps.</param>
    /// <param name="unitOfWork">Creates units of work for the deepest floor.</param>
    /// <param name="nodes">Forgets the nodes of a floor nobody is on.</param>
    /// <param name="groundItems">Forgets the drops of a floor nobody is on.</param>
    public MineService(IMapPresence presence, IMapCatalog maps, IPlayerService players, IUnitOfWorkFactory unitOfWork, IResourceNodeService nodes, IGroundItems groundItems)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(groundItems);

        _presence = presence;
        _maps = maps;
        _players = players;
        _unitOfWork = unitOfWork;
        _nodes = nodes;
        _groundItems = groundItems;
        _presence.PlayerJoined += this.OnPlayerJoinedAsync;
        _presence.PlayerLeft += this.OnPlayerLeftAsync;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _presence.PlayerJoined -= this.OnPlayerJoinedAsync;
        _presence.PlayerLeft -= this.OnPlayerLeftAsync;
    }

    /// <inheritdoc/>
    public async Task<MineView> GetAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return new MineView(MineStatus.Ok, 0, 0);
        }

        return new MineView(MineStatus.Ok, FloorOf(player), await this.DeepestAsync(player.AccountId, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public async Task<MineView> RideElevatorAsync(IConnection connection, int floor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return new MineView(MineStatus.NotAtElevator, 0, 0);
        }

        if (floor == MineRules.ReturnHomeStop)
        {
            if (!this.IsAtElevator(player) && !this.IsAtFloorWayUp(player))
            {
                return new MineView(MineStatus.NotAtElevator, 0, 0);
            }

            int homeDeepest = await this.DeepestAsync(player.AccountId, cancellationToken).ConfigureAwait(false);
            TravelStatus homeTravel = await _players.TravelAsync(
                connection,
                new MapInstance(GameMaps.FarmHouse, player.AccountId),
                cancellationToken).ConfigureAwait(false);
            return homeTravel == TravelStatus.Arrived
                ? new MineView(MineStatus.Ok, MineRules.EntranceFloor, homeDeepest)
                : new MineView(MineStatus.Busy, FloorOf(player), homeDeepest);
        }

        bool atElevator = this.IsAtElevator(player);
        bool atWayUp = this.IsAtFloorWayUp(player);
        if (!atElevator && !atWayUp)
        {
            return new MineView(MineStatus.NotAtElevator, 0, 0);
        }

        int deepest = await this.DeepestAsync(player.AccountId, cancellationToken).ConfigureAwait(false);
        int here = FloorOf(player);
        if (!MineRules.CanRide(deepest, floor))
        {
            return new MineView(MineStatus.NotUnlocked, here, deepest);
        }

        if (floor == here && (floor != MineRules.EntranceFloor || string.Equals(player.MapId, GameMaps.Mine, StringComparison.Ordinal)))
        {
            return new MineView(MineStatus.AlreadyThere, here, deepest);
        }

        MapInstance destination = floor == MineRules.EntranceFloor
            ? MapInstance.Shared(GameMaps.Mine)
            : new MapInstance(MineRules.FloorMapId(floor), player.AccountId);
        TravelStatus travel = await _players.TravelAsync(connection, destination, cancellationToken).ConfigureAwait(false);
        return travel == TravelStatus.Arrived
            ? new MineView(MineStatus.Ok, floor, deepest)
            : new MineView(MineStatus.Busy, here, deepest);
    }

    /// <inheritdoc/>
    public async Task<bool> DescendAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return false;
        }

        int floor = FloorOf(player);
        if (floor < 1 || !MineRules.HasLadder(floor))
        {
            return false;
        }

        TravelStatus travel = await _players.TravelAsync(connection, new MapInstance(MineRules.FloorMapId(floor + 1), player.AccountId), cancellationToken).ConfigureAwait(false);
        return travel == TravelStatus.Arrived;
    }

    // The floor number of the place a player stands in, or zero outside the floors (the entrance cave counts as zero).
    private static int FloorOf(PlayerPresence player) => GameMaps.TryGet(player.MapId, out MapSpec? spec) ? spec.MineFloor : 0;

    // The elevator of the map the player stands on, within reach of its tile (the same reach as a shop counter); the position is the server's own record.
    private bool IsAtElevator(PlayerPresence player)
    {
        if (!GameMaps.TryGet(player.MapId, out MapSpec? spec) || spec.Elevator is not { } elevator || !_maps.TryGet(player.MapId, out MapDefinition? map))
        {
            return false;
        }

        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        return ShopRules.InReach(new ShopPoint(0, elevator.TileX, elevator.TileY), feetX, feetY, map.Grid.TileSize);
    }

    // Every mine floor (mine1..mine40) has its way-up ladder/shaft at the floor's arrival tile.
    private bool IsAtFloorWayUp(PlayerPresence player)
    {
        if (!GameMaps.TryGet(player.MapId, out MapSpec? spec) || spec.MineFloor < 1 || !_maps.TryGet(player.MapId, out MapDefinition? map))
        {
            return false;
        }

        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        return ShopRules.InReach(new ShopPoint(0, spec.SpawnTileX, spec.SpawnTileY), feetX, feetY, map.Grid.TileSize);
    }

    private async Task<int> DeepestAsync(Guid playerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            PlayerMine? row = await unit.Repository<PlayerMine>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            return row?.DeepestFloor ?? 0;
        }
    }

    // Arriving on a floor deeper than any before is written down. Saving is best effort: a failed write only means the elevator learns it on the next arrival.
    [SuppressMessage("Design", "CA1031", Justification = "Raised from the join path; a failed save must not stop the player from arriving.")]
    private async ValueTask OnPlayerJoinedAsync(PlayerPresence player)
    {
        int floor = FloorOf(player);
        if (floor < 1)
        {
            return;
        }

        try
        {
            // The first player on a floor finds nothing lying on it: what a crash left behind belongs to a floor that is gone.
            List<PlayerPresence> present = [];
            _presence.CopyPlayers(player.Key, present);
            if (present.Count <= 1)
            {
                await _groundItems.ForgetAsync(player.Instance, CancellationToken.None).ConfigureAwait(false);
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                IUnitOfWork unit = _unitOfWork.Create();
                await using (unit.ConfigureAwait(false))
                {
                    IRepository<PlayerMine> rows = unit.Repository<PlayerMine>();
                    PlayerMine? row = await rows.FindAsync(player.AccountId, CancellationToken.None).ConfigureAwait(false);
                    if (row is { } known && known.DeepestFloor >= floor)
                    {
                        return;
                    }

                    if (row is null)
                    {
                        rows.Add(new PlayerMine { PlayerId = player.AccountId, DeepestFloor = floor });
                    }
                    else
                    {
                        row.DeepestFloor = floor;
                    }

                    try
                    {
                        _ = await unit.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception ex) when (unit.IsUniqueViolation(ex) || unit.IsConcurrencyConflict(ex))
                    {
                        // Two arrivals raced for the first row: read again.
                    }
                }
            }
        }
        catch (Exception)
        {
            // See above.
        }
    }

    // A floor nobody stands on is dropped: its nodes and what lay on it (docs/adr/ADR-026 D3). The same floor is built again from its seed on the next visit.
    [SuppressMessage("Design", "CA1031", Justification = "Raised from the leave path (including a disconnect); cleanup is best effort.")]
    private async ValueTask OnPlayerLeftAsync(PlayerPresence player)
    {
        if (!player.Instance.IsPrivate || FloorOf(player) < 1)
        {
            return;
        }

        List<PlayerPresence> remaining = [];
        _presence.CopyPlayers(player.Key, remaining);
        if (remaining.Count > 0)
        {
            return;
        }

        try
        {
            _nodes.Forget(player.Instance);
            await _groundItems.ForgetAsync(player.Instance, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Memory is dropped first; rows left behind are deleted when the floor is next forgotten.
        }
    }
}
