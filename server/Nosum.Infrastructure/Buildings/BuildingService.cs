// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Buildings;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Building;
using Nosum.Contracts.Building.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Domain.Buildings;
using Nosum.Domain.Coops;
using Nosum.Domain.Farm;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;
using Nosum.Gameplay.Players;
using Nosum.Gameplay.Shops;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Buildings;

/// <summary>
/// <see cref="IBuildingService"/> and the placement behaviour of buildings (docs/adr/ADR-028). A building is a placed object whose blueprint the carpenter sold for coins, wood and stone
/// in one transfer; placing it adds a <see cref="FarmBuilding"/> row in the same transaction, and it is finished two game days later, computed from the stored time. What the animals did
/// overnight is settled lazily the first time anybody touches the building on a new game day (<see cref="AnimalRules.Settle"/>), so nothing ticks and an unvisited building costs nothing.
/// Everything that changes an animal or the bench takes the building's lock first and re-checks inside the transaction, so produce is collected exactly once however many players ask.
/// </summary>
public sealed partial class BuildingService : IBuildingService, IPlacedBehaviour
{
    private const string CommissionReason = "building.commission";
    private const string AnimalBuyReason = "animal.buy";
    private const string FeedReason = "animal.feed";
    private const string CollectReason = "animal.collect";

    private readonly IMapPresence _presence;
    private readonly IChunkPublisher _publisher;
    private readonly IMapCatalog _maps;
    private readonly IInventoryService _inventory;
    private readonly ITransferService _transfers;
    private readonly IBuildingDirectory _buildings;
    private readonly IAnimalWander _wander;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ICoopMembership? _coops;
    private readonly GameCalendar _calendar;
    private readonly TimeProvider _time;
    private readonly StripedAsyncLock _locks = new(32);

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands where; every request uses the server's own record of the player.</param>
    /// <param name="publisher">Tells the players inside a building about an animal that changed, to the chunk the animal is in (docs/adr/ADR-022 D4); the buildings of a land go to everybody on it.</param>
    /// <param name="maps">The hosted maps.</param>
    /// <param name="inventory">Feed and produce move through it.</param>
    /// <param name="transfers">Coins and items of a commission or a purchase move through it in one transaction.</param>
    /// <param name="buildings">Knows the buildings of every land in use; told when one is placed.</param>
    /// <param name="wander">Where the animals are.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="coops">Co-op membership, asked for buildings on co-op land. <see langword="null"/> when there is no co-op land.</param>
    /// <param name="calendar">The server calendar: how long two game days are, and which day it is.</param>
    /// <param name="time">The server clock; tests pass a fake one.</param>
    public BuildingService(
        IMapPresence presence,
        IChunkPublisher publisher,
        IMapCatalog maps,
        IInventoryService inventory,
        ITransferService transfers,
        IBuildingDirectory buildings,
        IAnimalWander wander,
        IUnitOfWorkFactory unitOfWork,
        ICoopMembership? coops,
        GameCalendar calendar,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(buildings);
        ArgumentNullException.ThrowIfNull(wander);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(time);

        _presence = presence;
        _publisher = publisher;
        _maps = maps;
        _inventory = inventory;
        _transfers = transfers;
        _buildings = buildings;
        _wander = wander;
        _unitOfWork = unitOfWork;
        _coops = coops;
        _calendar = calendar;
        _time = time;
    }

    /// <inheritdoc/>
    PlaceableKind IPlacedBehaviour.Kind => PlaceableKind.Building;

    /// <inheritdoc/>
    public async Task<BuildingOutcome> CommissionAsync(IConnection connection, BuildingKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!BuildingCatalog.TryGet(kind, out BuildingDefinition? definition))
        {
            return BuildingOutcome.Refused(BuildingStatus.Unknown);
        }

        if (!this.TryPlayer(connection, out PlayerPresence? player) || !this.IsAtCarpenter(player))
        {
            return BuildingOutcome.Refused(BuildingStatus.NotAtShop);
        }

        // Coins, wood, stone and the blueprint change hands in one transaction: either the player has the blueprint and paid, or nothing happened.
        TransferParty party = TransferParty.Player(player.AccountId);
        bool limitReached = false;
        TransferStatus status = await _transfers.ExecuteAsync(
            [
                new TransferLeg(party, ItemCatalog.Wood, -definition.Wood, -definition.Coins),
                new TransferLeg(party, ItemCatalog.Stone, -definition.Stone),
                new TransferLeg(party, definition.BlueprintItemId, 1),
            ],
            CommissionReason,
            async (unit, ct) =>
            {
                CoopMembershipInfo? member = _coops is not null ? await _coops.FindAsync(player.AccountId, ct).ConfigureAwait(false) : null;
                int maxBuildings = member is not null ? BuildingRules.MaxPerLand * 2 : BuildingRules.MaxPerLand;
                int count = member is not null
                    ? await unit.Repository<FarmBuilding>().CountAsync(b => b.OwnerId == player.AccountId || b.OwnerId == member.CoopId, ct).ConfigureAwait(false)
                    : await unit.Repository<FarmBuilding>().CountAsync(b => b.OwnerId == player.AccountId, ct).ConfigureAwait(false);
                if (count >= maxBuildings)
                {
                    limitReached = true;
                    return TransferStatus.Refused;
                }

                return TransferStatus.Ok;
            },
            cancellationToken).ConfigureAwait(false);
        return status == TransferStatus.Ok
            ? new BuildingOutcome(BuildingStatus.Ok, Guid.Empty, definition.BlueprintItemId, 1)
            : BuildingOutcome.Refused(limitReached ? BuildingStatus.LimitReached : ToStatus(status));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BuildingView>> ListAsync(IConnection connection, bool mine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!this.TryPlayer(connection, out PlayerPresence? player))
        {
            return [];
        }

        List<MapInstance> lands = [];
        if (mine)
        {
            lands.Add(new MapInstance(GameMaps.Farm, player.AccountId));
            if (_coops is not null && await _coops.FindAsync(player.AccountId, cancellationToken).ConfigureAwait(false) is { } member)
            {
                lands.Add(MapInstance.OfCoop(GameMaps.CoopFarm, member.CoopId));
            }
        }
        else if (GameMaps.TryGet(player.MapId, out MapSpec? spec) && spec.Farmable && player.Instance.IsPrivate)
        {
            lands.Add(player.Instance);
        }

        long nowMs = this.NowMs();
        List<BuildingView> views = [];
        foreach (MapInstance land in lands)
        {
            await _buildings.PrepareAsync(land, cancellationToken).ConfigureAwait(false);
            views.AddRange(await this.ViewsOfAsync(_buildings.BuildingsOf(land), nowMs, cancellationToken).ConfigureAwait(false));
        }

        return views;
    }

    /// <inheritdoc/>
    void IPlacedBehaviour.JoinPlace(PlacedObject row, IUnitOfWork unit, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(unit);

        if (!BuildingCatalog.TryGetByBlueprint(row.ItemId, out BuildingDefinition? definition))
        {
            return;
        }

        long nowMs = now.ToUnixTimeMilliseconds();
        unit.Repository<FarmBuilding>().Add(new FarmBuilding
        {
            Placed = row,
            MapId = row.MapId,
            OwnerId = row.OwnerId,
            Kind = (byte)definition.Kind,
            CompletesAtMs = BuildingRules.CompletesAtMs(nowMs, _calendar),
            Rations = 0,
            SettledDay = _calendar.DayNumberAt(nowMs),
        });
    }

    /// <inheritdoc/>
    async ValueTask IPlacedBehaviour.PlacedAsync(MapInstance instance, PlacedItem item, CancellationToken cancellationToken)
    {
        IReadOnlyList<BuildingInfo> known = await _buildings.ReloadAsync(instance, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<BuildingView> views = await this.ViewsOfAsync(known, this.NowMs(), cancellationToken).ConfigureAwait(false);

        using PacketScope<BuildingsSnapshot> lease = PacketFactory<BuildingsSnapshot>.Acquire();
        BuildingsSnapshot packet = lease.Value;
        packet.Mine = false;
        packet.Buildings.Clear();
        foreach (BuildingView view in views)
        {
            packet.Buildings.Add(view.ToRow());
        }

        // A land's list of buildings is small and replaces the client's whole list, so it goes to everybody on the land, not to a chunk.
        await _publisher.PublishInstanceAsync(instance, packet, null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    IReadOnlyList<Guid> IPlacedBehaviour.OwnersOf(PlacedItem item) => [];

    /// <inheritdoc/>
    Task<PlaceStatus> IPlacedBehaviour.JoinPickUpAsync(PlacedItem item, IUnitOfWork unit, CancellationToken cancellationToken) => Task.FromResult(PlaceStatus.Fixed);

    /// <inheritdoc/>
    ValueTask IPlacedBehaviour.PickedUpAsync(MapInstance instance, PlacedItem item, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    private static BuildingStatus ToStatus(TransferStatus status) => status switch
    {
        TransferStatus.NotEnoughCoins => BuildingStatus.NotEnoughCoins,
        TransferStatus.NotEnoughItems => BuildingStatus.NotEnoughItems,
        TransferStatus.NoSpace => BuildingStatus.NoSpace,
        TransferStatus.Invalid => BuildingStatus.Unknown,
        _ => BuildingStatus.Conflict,
    };

    private static BuildingStatus ToStatus(Nosum.Contracts.Inventory.InventoryStatus status) => status switch
    {
        Nosum.Contracts.Inventory.InventoryStatus.NoSpace => BuildingStatus.NoSpace,
        Nosum.Contracts.Inventory.InventoryStatus.NotEnough => BuildingStatus.NotEnoughItems,
        _ => BuildingStatus.Conflict,
    };

    private long NowMs() => _time.GetUtcNow().ToUnixTimeMilliseconds();

    private bool TryPlayer(IConnection connection, out PlayerPresence player)
    {
        if (_presence.TryGet(connection, out PlayerPresence? found))
        {
            player = found;
            return true;
        }

        player = null!;
        return false;
    }

    // The player's feet are the server's own record; the counter is where the Town map says it is.
    private bool IsAtCarpenter(PlayerPresence player)
    {
        if (!GameMaps.TryGet(player.MapId, out MapSpec? spec) || !_maps.TryGet(player.MapId, out MapDefinition? map))
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

        foreach (ShopPoint point in spec.Shops)
        {
            if (point.ShopId == ShopCatalog.CarpenterShop && ShopRules.InReach(point, feetX, feetY, map.Grid.TileSize))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<List<BuildingView>> ViewsOfAsync(IReadOnlyList<BuildingInfo> infos, long nowMs, CancellationToken cancellationToken)
    {
        List<BuildingView> views = new(infos.Count);
        if (infos.Count == 0)
        {
            return views;
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            foreach (BuildingInfo info in infos)
            {
                if (!BuildingCatalog.TryGet(info.Kind, out BuildingDefinition? definition))
                {
                    continue;
                }

                Guid id = info.Id;
                int animals = await unit.Repository<FarmAnimal>().CountAsync(a => a.BuildingId == id, cancellationToken).ConfigureAwait(false);
                views.Add(new BuildingView(info.Id, info.ObjectId, info.Kind, info.Land.MapId, BuildingRules.RemainingMs(info.CompletesAtMs, nowMs), animals, definition.Capacity));
            }
        }

        return views;
    }
}
