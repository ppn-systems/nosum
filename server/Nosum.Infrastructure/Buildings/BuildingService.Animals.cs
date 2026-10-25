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
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Building;
using Nosum.Contracts.Building.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Domain.Buildings;
using Nosum.Domain.Coops;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;
using Nosum.Gameplay.Players;

namespace Nosum.Infrastructure.Buildings;

/// <summary>The animal side of <see cref="BuildingService"/>: buying, feeding, petting and collecting.</summary>
public sealed partial class BuildingService
{
    // Tile size of the interior maps, pixels: the same 16 as every map.
    private const int ChunkTileSize = 16;

    /// <inheritdoc/>
    public async Task<BuildingOutcome> BuyAnimalAsync(IConnection connection, Guid buildingId, AnimalKind kind, string? name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!AnimalCatalog.TryGet(kind, out AnimalDefinition? animal))
        {
            return BuildingOutcome.Refused(BuildingStatus.Unknown);
        }

        string cleanName = NicknameRules.Normalize(name);
        if (!AnimalRules.IsValidName(cleanName))
        {
            return BuildingOutcome.Refused(BuildingStatus.BadName);
        }

        if (!this.TryPlayer(connection, out PlayerPresence? player) || !this.IsAtCarpenter(player))
        {
            return BuildingOutcome.Refused(BuildingStatus.NotAtShop);
        }

        BuildingInfo? building = await _buildings.FindAsync(buildingId, cancellationToken).ConfigureAwait(false);
        if (building is null || !BuildingCatalog.TryGet(building.Kind, out BuildingDefinition? definition))
        {
            return BuildingOutcome.Refused(BuildingStatus.Unknown);
        }

        (bool mayTend, _) = await this.MayTendAsync(building.Land, player.AccountId, cancellationToken).ConfigureAwait(false);
        if (!mayTend)
        {
            return BuildingOutcome.Refused(BuildingStatus.NotYours);
        }

        long nowMs = this.NowMs();
        FarmAnimal row = new() { BuildingId = building.Id, Kind = (byte)kind, Name = cleanName, BoughtAt = _time.GetUtcNow() };
        BuildingStatus failure = BuildingStatus.Ok;
        using (await _locks.EnterAsync(building.Id, cancellationToken).ConfigureAwait(false))
        {
            TransferStatus status = await _transfers.ExecuteAsync(
                [new TransferLeg(TransferParty.Player(player.AccountId), 0, 0, -animal.Coins)],
                AnimalBuyReason,
                async (unit, ct) =>
                {
                    // Counted inside the transaction, under the building's lock: two purchases for the last place cannot both succeed.
                    int animals = await unit.Repository<FarmAnimal>().CountAsync(a => a.BuildingId == row.BuildingId, ct).ConfigureAwait(false);
                    BuildingVerdict verdict = BuildingRules.CanHouse(definition, kind, building.StateAt(nowMs), animals);
                    if (verdict != BuildingVerdict.Ok)
                    {
                        failure = verdict switch
                        {
                            BuildingVerdict.NotFinished => BuildingStatus.NotFinished,
                            BuildingVerdict.WrongResident => BuildingStatus.WrongResident,
                            _ => BuildingStatus.Full,
                        };
                        return TransferStatus.Refused;
                    }

                    unit.Repository<FarmAnimal>().Add(row);
                    return TransferStatus.Ok;
                },
                cancellationToken).ConfigureAwait(false);
            if (status != TransferStatus.Ok)
            {
                return BuildingOutcome.Refused(status == TransferStatus.Refused && failure != BuildingStatus.Ok ? failure : ToStatus(status));
            }
        }

        // The new animal appears in its room for whoever is inside.
        await this.PushAsync(building, definition, row.Id, this.NowMs(), cancellationToken).ConfigureAwait(false);
        return new BuildingOutcome(BuildingStatus.Ok, row.Id, 0, 0);
    }

    /// <inheritdoc/>
    public async Task<AnimalRoomView?> GetAnimalsAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!this.TryPlayer(connection, out PlayerPresence? player))
        {
            return null;
        }

        Inside inside = await this.InsideAsync(player, cancellationToken).ConfigureAwait(false);
        if (inside.Building is null || inside.Definition is null)
        {
            return null;
        }

        long nowMs = this.NowMs();
        using (await _locks.EnterAsync(inside.Building.Id, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                Room? room = await this.LoadRoomAsync(unit, inside.Building.Id, nowMs, cancellationToken).ConfigureAwait(false);
                return room is null
                    ? null
                    : new AnimalRoomView(inside.Building.Id, inside.Building.Kind, room.Building.Rations, inside.MayTend, this.ViewsOf(inside.Building.Kind, room, nowMs));
            }
        }
    }

    /// <inheritdoc/>
    public async Task<BuildingOutcome> FeedAsync(IConnection connection, int slot, int quantity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!this.TryPlayer(connection, out PlayerPresence? player))
        {
            return BuildingOutcome.Refused(BuildingStatus.NotInside);
        }

        Inside inside = await this.InsideAsync(player, cancellationToken).ConfigureAwait(false);
        if (inside.Building is null || inside.Definition is null)
        {
            return BuildingOutcome.Refused(BuildingStatus.NotInside);
        }

        if (!inside.MayTend)
        {
            return BuildingOutcome.Refused(BuildingStatus.NotYours);
        }

        if (quantity < 1 || quantity > AnimalRules.MaxFeedAtOnce || !this.TroughInReach(player, inside.Definition))
        {
            return BuildingOutcome.Refused(quantity < 1 || quantity > AnimalRules.MaxFeedAtOnce ? BuildingStatus.NotFeed : BuildingStatus.OutOfReach);
        }

        InventorySlotView held = await _inventory.PeekSlotAsync(player.AccountId, slot, cancellationToken).ConfigureAwait(false);
        int perItem = AnimalRules.RationsOf(held.ItemId);
        if (held.ItemId == 0 || held.Quantity < 1 || perItem == 0)
        {
            return BuildingOutcome.Refused(BuildingStatus.NotFeed);
        }

        BuildingInfo building = inside.Building;
        long nowMs = this.NowMs();
        int resulting = 0;
        using (await _locks.EnterAsync(building.Id, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork readUnit = _unitOfWork.Create();
            Room? room;
            await using (readUnit.ConfigureAwait(false))
            {
                room = await this.LoadRoomAsync(readUnit, building.Id, nowMs, cancellationToken).ConfigureAwait(false);
            }

            if (room is null)
            {
                return BuildingOutcome.Refused(BuildingStatus.NotInside);
            }

            int take = Math.Min(Math.Min(quantity, held.Quantity), (AnimalRules.MaxRations - room.Building.Rations) / perItem);
            if (take < 1)
            {
                return BuildingOutcome.Refused(BuildingStatus.BenchFull);
            }

            int added = take * perItem;
            BuildingStatus failure = BuildingStatus.Ok;
            InventoryChange change = await _inventory.ApplyAsync(
                player.AccountId,
                [InventoryOperation.RemoveFromSlot(slot, held.ItemId, take)],
                FeedReason,
                async (unit, ct) =>
                {
                    FarmBuilding? row = await unit.Repository<FarmBuilding>().FindAsync(building.Id, ct).ConfigureAwait(false);
                    if (row is null || row.Rations + added > AnimalRules.MaxRations)
                    {
                        failure = BuildingStatus.BenchFull;
                        return false;
                    }

                    row.Rations += added;
                    resulting = row.Rations;
                    this.Audit(unit, inside.Member, held.ItemId, -take, CoopLand.FeedReason);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            if (!change.IsOk)
            {
                return BuildingOutcome.Refused(failure != BuildingStatus.Ok ? failure : ToStatus(change.Status));
            }
        }

        await this.PushAsync(building, inside.Definition, null, this.NowMs(), cancellationToken).ConfigureAwait(false);
        return new BuildingOutcome(BuildingStatus.Ok, Guid.Empty, held.ItemId, resulting);
    }

    /// <inheritdoc/>
    public async Task<AnimalOutcome> PetAsync(IConnection connection, Guid animalId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        (AnimalOutcome? refusal, PlayerPresence? player, Inside inside) = await this.AuthorizeTendAsync(connection, cancellationToken).ConfigureAwait(false);
        if (refusal is { } refused || player is null || inside.Building is null || inside.Definition is null)
        {
            return refusal ?? AnimalOutcome.Refused(BuildingStatus.NotInside);
        }

        BuildingInfo building = inside.Building;
        long nowMs = this.NowMs();
        int friendship;
        using (await _locks.EnterAsync(building.Id, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                Room? room = await this.LoadRoomAsync(unit, building.Id, nowMs, cancellationToken).ConfigureAwait(false);
                FarmAnimal? animal = room?.Animals.Find(a => a.Id == animalId);
                if (room is null || animal is null)
                {
                    return AnimalOutcome.Refused(BuildingStatus.Unknown);
                }

                if (!this.AnimalInReach(player, building.Kind, animal.Id, nowMs))
                {
                    return AnimalOutcome.Refused(BuildingStatus.OutOfReach);
                }

                if (!AnimalRules.CanPet(animal.PettedDay, room.Today))
                {
                    return AnimalOutcome.Refused(BuildingStatus.AlreadyPetted);
                }

                animal.PettedDay = room.Today;
                animal.Friendship = AnimalRules.AfterPetting(animal.Friendship);
                friendship = animal.Friendship;
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await this.PushAsync(building, inside.Definition, animalId, this.NowMs(), cancellationToken).ConfigureAwait(false);
        return new AnimalOutcome(BuildingStatus.Ok, 0, 0, friendship);
    }

    /// <inheritdoc/>
    public async Task<AnimalOutcome> CollectAsync(IConnection connection, Guid animalId, int slot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        (AnimalOutcome? refusal, PlayerPresence? player, Inside inside) = await this.AuthorizeTendAsync(connection, cancellationToken).ConfigureAwait(false);
        if (refusal is { } refused || player is null || inside.Building is null || inside.Definition is null)
        {
            return refusal ?? AnimalOutcome.Refused(BuildingStatus.NotInside);
        }

        BuildingInfo building = inside.Building;
        long nowMs = this.NowMs();
        int itemId;
        int friendship;
        using (await _locks.EnterAsync(building.Id, cancellationToken).ConfigureAwait(false))
        {
            Room? room;
            FarmAnimal? animal;
            IUnitOfWork readUnit = _unitOfWork.Create();
            await using (readUnit.ConfigureAwait(false))
            {
                room = await this.LoadRoomAsync(readUnit, building.Id, nowMs, cancellationToken).ConfigureAwait(false);
                animal = room?.Animals.Find(a => a.Id == animalId);
            }

            if (room is null || animal is null || !AnimalCatalog.TryGet((AnimalKind)animal.Kind, out AnimalDefinition? species))
            {
                return AnimalOutcome.Refused(BuildingStatus.Unknown);
            }

            if (!this.AnimalInReach(player, building.Kind, animal.Id, nowMs))
            {
                return AnimalOutcome.Refused(BuildingStatus.OutOfReach);
            }

            if (!AnimalRules.HasProduce(animal.ProduceDay, animal.CollectedDay, room.Today))
            {
                return AnimalOutcome.Refused(BuildingStatus.NoProduce);
            }

            if (species.NeedsPail)
            {
                InventorySlotView held = await _inventory.PeekSlotAsync(player.AccountId, slot, cancellationToken).ConfigureAwait(false);
                if (held.ItemId != ItemCatalog.MilkPail)
                {
                    return AnimalOutcome.Refused(BuildingStatus.NeedsPail);
                }
            }

            int today = room.Today;
            friendship = animal.Friendship;
            itemId = AnimalRules.ProduceOf(species, animal.Id, today, friendship);
            BuildingStatus failure = BuildingStatus.Ok;
            InventoryChange change = await _inventory.ApplyAsync(
                player.AccountId,
                [InventoryOperation.Add(itemId, 1)],
                CollectReason,
                async (unit, ct) =>
                {
                    // The row is read again inside the transaction: whoever collected first has set the day, and this one finds nothing left.
                    FarmAnimal? row = await unit.Repository<FarmAnimal>().FindAsync(animalId, ct).ConfigureAwait(false);
                    if (row is null || !AnimalRules.HasProduce(row.ProduceDay, row.CollectedDay, today))
                    {
                        failure = BuildingStatus.NoProduce;
                        return false;
                    }

                    row.CollectedDay = today;
                    this.Audit(unit, inside.Member, itemId, 1, CoopLand.CollectReason);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            if (!change.IsOk)
            {
                return AnimalOutcome.Refused(failure != BuildingStatus.Ok ? failure : ToStatus(change.Status));
            }
        }

        await this.PushAsync(building, inside.Definition, animalId, this.NowMs(), cancellationToken).ConfigureAwait(false);
        return new AnimalOutcome(BuildingStatus.Ok, itemId, 1, friendship);
    }

    // Where the player stands in terms of buildings: the building whose interior it is, and whether they may tend it.
    private async Task<Inside> InsideAsync(PlayerPresence player, CancellationToken cancellationToken)
    {
        if (!GameMaps.TryGet(player.MapId, out MapSpec? spec) || !spec.IsInterior || !BuildingCatalog.TryGet(spec.InteriorOf, out BuildingDefinition? definition))
        {
            return default;
        }

        BuildingInfo? building = await _buildings.FindAsync(player.Instance.OwnerId, cancellationToken).ConfigureAwait(false);
        if (building is null || building.Kind != spec.InteriorOf)
        {
            return default;
        }

        (bool mayTend, CoopMembershipInfo? member) = await this.MayTendAsync(building.Land, player.AccountId, cancellationToken).ConfigureAwait(false);
        return new Inside(building, definition, mayTend, member);
    }

    // The owner of a private farm and the members of a co-op on its land tend the animals; a visitor looks (docs/adr/ADR-019, ADR-028 D2).
    private async Task<(bool MayTend, CoopMembershipInfo? Member)> MayTendAsync(MapInstance land, Guid accountId, CancellationToken cancellationToken)
    {
        CoopMembershipInfo? member = null;
        if (land.IsCoopLand && _coops is not null)
        {
            CoopMembershipInfo? info = await _coops.FindAsync(accountId, cancellationToken).ConfigureAwait(false);
            member = info is not null && info.CoopId == land.OwnerId ? info : null;
        }

        return (PlacedAccess.MayUse(land, accountId, member is not null), member);
    }

    private async Task<(AnimalOutcome? Refusal, PlayerPresence? Player, Inside Inside)> AuthorizeTendAsync(IConnection connection, CancellationToken cancellationToken)
    {
        if (!this.TryPlayer(connection, out PlayerPresence? player))
        {
            return (AnimalOutcome.Refused(BuildingStatus.NotInside), null, default);
        }

        Inside inside = await this.InsideAsync(player, cancellationToken).ConfigureAwait(false);
        if (inside.Building is null)
        {
            return (AnimalOutcome.Refused(BuildingStatus.NotInside), null, default);
        }

        return inside.MayTend ? (null, player, inside) : (AnimalOutcome.Refused(BuildingStatus.NotYours), null, default);
    }

    // Within reach of any tile of the feeding bench.
    private bool TroughInReach(PlayerPresence player, BuildingDefinition definition)
    {
        if (!_maps.TryGet(definition.Key, out MapDefinition? map))
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

        for (int x = definition.TroughLeft; x < definition.TroughLeft + definition.TroughWidth; x++)
        {
            if (FarmRules.InReach(feetX, feetY, x, definition.TroughTop, map.Grid.TileSize))
            {
                return true;
            }
        }

        return false;
    }

    private bool AnimalInReach(PlayerPresence player, BuildingKind kind, Guid animalId, long nowMs)
    {
        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        AnimalMotionView where = _wander.Where(kind, animalId, nowMs);
        return AnimalRules.InReach(feetX, feetY, where.X, where.Y);
    }

    // The building's row with the overnight feeding settled for today, and its animals oldest first. Settling writes, so the caller holds the building's lock.
    private async Task<Room?> LoadRoomAsync(IUnitOfWork unit, Guid buildingId, long nowMs, CancellationToken cancellationToken)
    {
        FarmBuilding? building = await unit.Repository<FarmBuilding>().FindAsync(buildingId, cancellationToken).ConfigureAwait(false);
        if (building is null)
        {
            return null;
        }

        List<FarmAnimal> animals = await unit.Repository<FarmAnimal>().ListAsync(a => a.BuildingId == buildingId, cancellationToken).ConfigureAwait(false);
        animals.Sort(static (a, b) => a.BoughtAt != b.BoughtAt ? a.BoughtAt.CompareTo(b.BoughtAt) : a.Id.CompareTo(b.Id));
        int today = _calendar.DayNumberAt(nowMs);
        if (today > building.SettledDay)
        {
            FeedingSettlement settlement = AnimalRules.Settle(building.Rations, animals.Count, building.SettledDay, today);
            building.Rations = settlement.Rations;
            building.SettledDay = today;
            for (int i = 0; i < settlement.FedToday && i < animals.Count; i++)
            {
                animals[i].ProduceDay = today;
            }

            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new Room(building, animals, today);
    }

    private List<AnimalView> ViewsOf(BuildingKind kind, Room room, long nowMs)
    {
        List<AnimalView> views = new(room.Animals.Count);
        foreach (FarmAnimal animal in room.Animals)
        {
            views.Add(this.ViewOf(kind, animal, room.Today, nowMs));
        }

        return views;
    }

    private AnimalView ViewOf(BuildingKind kind, FarmAnimal animal, int today, long nowMs)
    {
        AnimalMotionView where = _wander.Where(kind, animal.Id, nowMs);
        byte flags = (byte)((animal.PettedDay == today ? AnimalView.PettedFlag : 0) | (AnimalRules.HasProduce(animal.ProduceDay, animal.CollectedDay, today) ? AnimalView.ProduceFlag : 0));
        return new AnimalView(animal.Id, (AnimalKind)animal.Kind, animal.Name, animal.Friendship, flags, where.X, where.Y, where.TargetX, where.TargetY, where.MoveMs);
    }

    // Tells the players inside the building how it stands now: the bench, and the one animal that changed. It goes to the chunk of that animal (or of the bench when no animal
    // changed): the inside of a building is within the interest set of everybody in it (docs/adr/ADR-022 D2, D4).
    private async Task PushAsync(BuildingInfo building, BuildingDefinition definition, Guid? animalId, long nowMs, CancellationToken cancellationToken)
    {
        Room? room;
        using (await _locks.EnterAsync(building.Id, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                room = await this.LoadRoomAsync(unit, building.Id, nowMs, cancellationToken).ConfigureAwait(false);
            }
        }

        if (room is null)
        {
            return;
        }

        using PacketScope<AnimalsChanged> lease = PacketFactory<AnimalsChanged>.Acquire();
        AnimalsChanged packet = lease.Value;
        packet.BuildingId = building.Id;
        packet.Rations = room.Building.Rations;
        packet.Upserted.Clear();
        packet.Removed.Clear();
        ChunkCoord chunk = ChunkCoord.OfTile(definition.TroughLeft, definition.TroughTop);
        foreach (FarmAnimal animal in room.Animals)
        {
            if (animal.Id == animalId)
            {
                AnimalView view = this.ViewOf(building.Kind, animal, room.Today, nowMs);
                packet.Upserted.Add(view.ToRow());
                chunk = ChunkCoord.OfPixels(view.X, view.Y, ChunkTileSize);
            }
        }

        await _publisher.PublishAsync(building.Interior, chunk, packet, null, cancellationToken).ConfigureAwait(false);
    }

    // Co-op land keeps an audit line for every change of what the members hold (docs/adr/ADR-020).
    private void Audit(IUnitOfWork unit, CoopMembershipInfo? member, int itemId, int delta, string reason)
    {
        if (member is null)
        {
            return;
        }

        unit.Repository<CoopLogEntry>().Add(new CoopLogEntry { CoopId = member.CoopId, MemberId = member.MemberId, ItemId = itemId, Delta = delta, Reason = reason, OccurredAt = _time.GetUtcNow() });
    }

    private sealed record Room(FarmBuilding Building, List<FarmAnimal> Animals, int Today);

    private readonly record struct Inside(BuildingInfo? Building, BuildingDefinition? Definition, bool MayTend, CoopMembershipInfo? Member);
}
