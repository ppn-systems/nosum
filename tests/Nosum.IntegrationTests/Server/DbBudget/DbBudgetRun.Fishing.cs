// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using System.Threading.Tasks;
using Nosum.Abstractions.Maps;
using Nosum.Contracts.Fishing;
using Nosum.Contracts.Fishing.C2S;
using Nosum.Contracts.Fishing.S2C;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;

namespace Nosum.IntegrationTests.Server.DbBudget;

public sealed partial class DbBudgetRun
{
    // A player with a rod on the Beach casts at the sea and reels in before the bite (docs/adr/ADR-071): the energy and the cast are one write.
    private async Task FishingAsync()
    {
        _ = BudgetRig.Service<IMapCatalog>().TryGet(GameMaps.Beach, out MapDefinition? beach);
        (int waterX, int waterY, int standX, int standY) = NodeWireTests.ShoreOf(beach!);
        await using BudgetPlayer p = await this.FreshAsync(GameMaps.Beach, (standX * 16) + 8, (standY * 16) + 8, 0, (ItemCatalog.FishingRod, 1));

        using (GetInventory look = new())
        {
            InventorySnapshot inventory = await p.RequestAsync<InventorySnapshot>(look);
            using SelectSlot choice = new() { Slot = (byte)inventory.Slots.Single(s => s.ItemId == ItemCatalog.FishingRod).Slot };
            _ = await p.RequestAsync<InventoryResult>(choice);
        }

        CastResult cast = await _rig.ProbeAsync<CastResult>(p, "Fishing", FishingOpCode.CastRod, "CastRod", "at the sea with a rod", () => new CastRod { TileX = waterX, TileY = waterY });
        _ = await _rig.ProbeAsync<ReelResult>(p, "Fishing", FishingOpCode.ReelIn, "ReelIn", "before the bite", () => new ReelIn { CastId = cast.CastId });
    }
}
