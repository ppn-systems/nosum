// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Inventory;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Coop.S2C;
using Nosum.Domain.Buildings;
using Nosum.Domain.Coops;
using Nosum.Domain.Farm;
using Nosum.Domain.Inventory;
using Nosum.Domain.Nodes;
using Nosum.Domain.Placeables;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;

namespace Nosum.Infrastructure.Coops;

public sealed partial class CoopService
{
    /// <inheritdoc/>
    public async Task<CoopStatus> DepositAsync(Guid playerId, int itemId, int quantity, CancellationToken cancellationToken = default)
    {
        CoopStatus invalid = ToStatus(CoopRules.ValidateStack(itemId, quantity));
        if (invalid != CoopStatus.Ok)
        {
            return invalid;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await this.FindMemberAsync(playerId, cancellationToken).ConfigureAwait(false) is not { } me)
            {
                return CoopStatus.NotInCoop;
            }

            // The warehouse holds so many item kinds (docs/adr/ADR-038); the gate keeps the count from changing under this check.
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                CoopRecord? coop = await _directory.OfPlayerAsync(playerId, cancellationToken).ConfigureAwait(false);
                List<CoopStockItem> held = await unit.Repository<CoopStockItem>().ListAsync(s => s.CoopId == me.CoopId, cancellationToken).ConfigureAwait(false);
                if (!CoopUpgrades.CanStore(coop?.WarehouseLevel ?? 0, held.Count, held.Exists(s => s.ItemId == itemId)))
                {
                    return CoopStatus.WarehouseFull;
                }
            }

            return await this.MoveAsync(me, itemId, quantity, deposit: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<CoopStatus> WithdrawAsync(Guid playerId, int itemId, int quantity, CancellationToken cancellationToken = default)
    {
        CoopStatus invalid = ToStatus(CoopRules.ValidateStack(itemId, quantity));
        if (invalid != CoopStatus.Ok)
        {
            return invalid;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await this.FindMemberAsync(playerId, cancellationToken).ConfigureAwait(false) is not { } me)
            {
                return CoopStatus.NotInCoop;
            }

            // Everything that changes the warehouse holds the gate, so what is read here is what the transfer will see.
            int inStock;
            long contributed;
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                CoopStockItem? row = await unit.Repository<CoopStockItem>().FirstOrDefaultAsync(s => s.CoopId == me.CoopId && s.ItemId == itemId, cancellationToken).ConfigureAwait(false);
                inStock = row?.Quantity ?? 0;
                List<CoopLogEntry> log = await unit.Repository<CoopLogEntry>().ListAsync(e => e.CoopId == me.CoopId && e.MemberId == me.Id && e.ItemId == itemId, cancellationToken).ConfigureAwait(false);
                contributed = log.Where(e => CoopLand.IsWarehouseReason(e.Reason)).Sum(e => (long)e.Delta);
            }

            if (quantity > inStock)
            {
                return CoopStatus.NotEnough;
            }

            if (CoopRules.CanWithdraw(me.Role == CoopRole.Founder, inStock, contributed, quantity) != CoopVerdict.Ok)
            {
                return CoopStatus.NotAllowed;
            }

            return await this.MoveAsync(me, itemId, quantity, deposit: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<CoopStatus> UpgradeAsync(Guid playerId, CoopUpgradeTrack track, int targetLevel, CancellationToken cancellationToken = default)
    {
        CoopTrack rulesTrack = track switch
        {
            CoopUpgradeTrack.Warehouse => CoopTrack.Warehouse,
            CoopUpgradeTrack.Land => CoopTrack.Land,
            _ => (CoopTrack)0,
        };

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await this.FindMemberAsync(playerId, cancellationToken).ConfigureAwait(false) is not { } me)
            {
                return CoopStatus.NotInCoop;
            }

            if (me.Role != CoopRole.Founder)
            {
                return CoopStatus.NotFounder;
            }

            // The gate serializes every change of the warehouse and of the levels, so what is read here is what the transfer sees; the enlisted work still re-checks the level.
            Guid coopId = me.CoopId;
            Guid memberId = me.Id;
            int current;
            Dictionary<int, int> stock;
            IUnitOfWork read = _unitOfWork.Create();
            await using (read.ConfigureAwait(false))
            {
                CoopRecord? coop = await _directory.OfPlayerAsync(playerId, cancellationToken).ConfigureAwait(false);
                if (coop is null)
                {
                    return CoopStatus.NotInCoop;
                }

                current = rulesTrack == CoopTrack.Land ? coop.LandLevel : coop.WarehouseLevel;
                stock = (await read.Repository<CoopStockItem>().ListAsync(s => s.CoopId == coopId, cancellationToken).ConfigureAwait(false)).ToDictionary(s => s.ItemId, s => s.Quantity);
            }

            UpgradeVerdict verdict = CoopUpgrades.CanBuy(rulesTrack, current, targetLevel, item => stock.GetValueOrDefault(item));
            if (verdict != UpgradeVerdict.Ok)
            {
                return verdict switch
                {
                    UpgradeVerdict.MaxLevel => CoopStatus.MaxLevel,
                    UpgradeVerdict.NotEnoughStock => CoopStatus.NotEnough,
                    _ => CoopStatus.AlreadyUpgraded,
                };
            }

            string reason = CoopUpgrades.ReasonOf(rulesTrack);
            List<TransferLeg> legs = [.. CoopUpgrades.Cost(rulesTrack, targetLevel).Select(c => new TransferLeg(TransferParty.Coop(coopId), c.ItemId, -c.Quantity))];

            // A transfer needs a player party to run in; the buyer takes part with a leg that moves nothing, and the price goes to nobody.
            List<TransferLeg> transfer = [new TransferLeg(TransferParty.Player(playerId), 0, 0), .. legs];
            bool stale = false;
            TransferStatus status = await _transfers.ExecuteAsync(
                transfer,
                reason,
                async (unit, token) =>
                {
                    Coop? coop = await unit.Repository<Coop>().FindAsync(coopId, token).ConfigureAwait(false);
                    int level = coop is null ? -1 : rulesTrack == CoopTrack.Land ? coop.LandLevel : coop.WarehouseLevel;
                    if (coop is null || level != targetLevel - 1)
                    {
                        stale = true;
                        return TransferStatus.Refused;
                    }

                    if (rulesTrack == CoopTrack.Land)
                    {
                        coop.LandLevel = targetLevel;
                    }
                    else
                    {
                        coop.WarehouseLevel = targetLevel;
                    }

                    DateTimeOffset now = _time.GetUtcNow();
                    foreach (TransferLeg leg in legs)
                    {
                        unit.Repository<CoopLogEntry>().Add(new CoopLogEntry { CoopId = coopId, MemberId = memberId, ItemId = leg.ItemId, Delta = leg.Items, Reason = reason, OccurredAt = now });
                    }

                    return TransferStatus.Ok;
                },
                cancellationToken).ConfigureAwait(false);

            // The level may have changed in the row whatever the status says (a lost race): the next read comes from the rows.
            _directory.Changed(coopId);
            if (status != TransferStatus.Ok)
            {
                return stale ? CoopStatus.AlreadyUpgraded : status == TransferStatus.NotEnoughItems ? CoopStatus.NotEnough : CoopStatus.Conflict;
            }

            await this.PushToAsync(await this.MemberPlayersAsync(playerId, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return CoopStatus.Ok;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async Task<CoopStatus> MoveAsync(Me me, int itemId, int quantity, bool deposit, CancellationToken cancellationToken)
    {
        int signed = deposit ? quantity : -quantity;
        Guid coopId = me.CoopId;
        Guid memberId = me.Id;
        TransferStatus status = await _transfers.ExecuteAsync(
            [
                new TransferLeg(TransferParty.Player(me.PlayerId), itemId, -signed),
                new TransferLeg(TransferParty.Coop(coopId), itemId, signed),
            ],
            deposit ? "coop.deposit" : "coop.withdraw",
            (unit, _) =>
            {
                unit.Repository<CoopLogEntry>().Add(new CoopLogEntry
                {
                    CoopId = coopId,
                    MemberId = memberId,
                    ItemId = itemId,
                    Delta = signed,
                    Reason = deposit ? "coop.deposit" : "coop.withdraw",
                    OccurredAt = _time.GetUtcNow(),
                });
                return Task.FromResult(TransferStatus.Ok);
            },
            cancellationToken).ConfigureAwait(false);

        if (status != TransferStatus.Ok)
        {
            return status switch
            {
                TransferStatus.NotEnoughItems => CoopStatus.NotEnough,
                TransferStatus.NoSpace => CoopStatus.NoSpace,
                _ => CoopStatus.Conflict,
            };
        }

        await this.PushToAsync(await this.MemberPlayersAsync(me.PlayerId, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        return CoopStatus.Ok;
    }

    // Items must not vanish with the land (docs/adr/ADR-024): a co-op whose chests hold items, or whose furnaces hold a job, or whose buildings hold animals or rations, is not dissolved, like one whose warehouse holds items.
    private async Task<bool> LandHoldsItemsAsync(IUnitOfWork unit, Guid coopId, CancellationToken cancellationToken)
    {
        List<PlacedObject> pieces = await unit.Repository<PlacedObject>().ListAsync(p => p.OwnerId == coopId, cancellationToken).ConfigureAwait(false);
        int[] furnaces = [.. pieces.Where(p => p.ItemId == ItemCatalog.Furnace).Select(p => p.Id)];
        Guid[] chests = [.. pieces.Where(p => p.ItemId == ItemCatalog.Chest).Select(p => ContainerIds.ForChest(p.Id))];
        if ((chests.Length > 0 && await _inventory.AnyContainerHoldsItemsAsync(unit, chests, cancellationToken).ConfigureAwait(false))
            || (furnaces.Length > 0 && await unit.Repository<FurnaceJob>().AnyAsync(j => furnaces.Contains(j.ObjectId), cancellationToken).ConfigureAwait(false)))
        {
            return true;
        }

        List<FarmBuilding> buildings = await unit.Repository<FarmBuilding>().ListAsync(b => b.OwnerId == coopId, cancellationToken).ConfigureAwait(false);
        if (buildings.Exists(b => b.Rations > 0))
        {
            return true;
        }

        Guid[] buildingIds = [.. buildings.Select(b => b.Id)];
        return buildingIds.Length > 0 && await unit.Repository<FarmAnimal>().AnyAsync(a => buildingIds.Contains(a.BuildingId), cancellationToken).ConfigureAwait(false);
    }

    // A dissolved co-op's land is wiped with the co-op (docs/adr/ADR-020 D6): the plots, the placed objects (their furnace jobs go by cascade) and the empty chests' inventories go in the same transaction as the co-op row.
    private async Task<List<MapInstance>> WipeLandAsync(IUnitOfWork unit, Guid coopId, CancellationToken cancellationToken)
    {
        List<MapInstance> wiped = [];
        foreach (MapSpec spec in GameMaps.All)
        {
            if (!spec.IsCoop)
            {
                continue;
            }

            IRepository<FarmPlot> plots = unit.Repository<FarmPlot>();
            foreach (FarmPlot plot in await plots.ListAsync(p => p.MapId == spec.Id && p.OwnerId == coopId, cancellationToken).ConfigureAwait(false))
            {
                plots.Remove(plot);
            }

            IRepository<PlacedObject> pieces = unit.Repository<PlacedObject>();
            List<PlacedObject> placed = await pieces.ListAsync(p => p.MapId == spec.Id && p.OwnerId == coopId, cancellationToken).ConfigureAwait(false);
            int[] chestIds = [.. placed.Where(p => p.ItemId == ItemCatalog.Chest).Select(p => p.Id)];
            if (chestIds.Length > 0)
            {
                IRepository<ChestLogEntry> chestLogs = unit.Repository<ChestLogEntry>();
                foreach (ChestLogEntry log in await chestLogs.ListAsync(l => chestIds.Contains(l.ObjectId), cancellationToken).ConfigureAwait(false))
                {
                    chestLogs.Remove(log);
                }
            }

            foreach (PlacedObject piece in placed)
            {
                if (piece.ItemId == ItemCatalog.Chest)
                {
                    _ = await _inventory.DropContainerAsync(unit, ContainerIds.ForChest(piece.Id), onlyIfEmpty: false, cancellationToken).ConfigureAwait(false);
                }

                pieces.Remove(piece);
            }

            IRepository<WorldNode> nodes = unit.Repository<WorldNode>();
            foreach (WorldNode node in await nodes.ListAsync(n => n.MapId == spec.Id && n.OwnerId == coopId, cancellationToken).ConfigureAwait(false))
            {
                nodes.Remove(node);
            }

            IRepository<NodeInstanceState> nodeStates = unit.Repository<NodeInstanceState>();
            foreach (NodeInstanceState state in await nodeStates.ListAsync(s => s.MapId == spec.Id && s.OwnerId == coopId, cancellationToken).ConfigureAwait(false))
            {
                nodeStates.Remove(state);
            }

            wiped.Add(MapInstance.OfCoop(spec.Id, coopId));
        }

        return wiped;
    }

    private static CoopStatus ToStatus(CoopVerdict verdict) => verdict switch
    {
        CoopVerdict.Ok => CoopStatus.Ok,
        CoopVerdict.InvalidName => CoopStatus.InvalidName,
        CoopVerdict.UnknownItem => CoopStatus.UnknownItem,
        CoopVerdict.NotTradable => CoopStatus.NotTradable,
        CoopVerdict.InvalidQuantity => CoopStatus.InvalidQuantity,
        _ => CoopStatus.NotAllowed,
    };
}
