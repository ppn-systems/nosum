// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using Nosum.Abstractions.Shops;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Coop.C2S;
using Nosum.Contracts.Coop.S2C;
using Nosum.Contracts.Requests;
using Nosum.Contracts.Requests.C2S;
using Nosum.Contracts.Requests.S2C;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;

namespace Nosum.IntegrationTests.Server.DbBudget;

public sealed partial class DbBudgetRun
{
    // The daily requests on the Town board (ADR-036).
    private async Task RequestsAsync()
    {
        (double x, double y) = TileCentre(42, 57);
        await using BudgetPlayer p = await this.FreshAsync(GameMaps.Town, x, y, 0);
        RequestsSnapshot board = await _rig.ProbeAsync<RequestsSnapshot>(p, "Requests", RequestsOpCode.GetRequests, "GetRequests", "today's board", () => new GetRequests());
        _ = await _rig.ProbeAsync<RequestsSnapshot>(p, "Requests", RequestsOpCode.GetRequests, "GetRequests", "today's board, asked again", () => new GetRequests());
        RequestRow? first = board.Rows.FirstOrDefault();
        if (first is null)
        {
            this.Failures.Add("requests: the board has no row today");
            return;
        }

        _ = await BudgetRig.Service<IDevGrantService>().GrantAsync(p.AccountId, first.ItemId, first.Quantity, 0, _rig.Timeout);
        int slot = first.Slot;
        int day = board.Day;
        _ = await _rig.ProbeAsync<RequestResult>(p, "Requests", RequestsOpCode.DeliverRequest, "DeliverRequest", "fill one request", () => new DeliverRequest { Day = day, Slot = slot });
    }

    // Upgrading the co-op warehouse with what the members stored (ADR-038): the founder pays from the co-op stock.
    private async Task CoopUpgradeAsync()
    {
        (double x, double y) = TileCentre(5, 54);
        await using BudgetPlayer founder = await this.FreshAsync(GameMaps.Town, x, y, registered: true, 0, (ItemCatalog.Wood, 50), (ItemCatalog.Stone, 25));
        using (CreateCoop create = new() { Name = "Upgrade Farmers" })
        {
            _ = await founder.RequestAsync<CoopResult>(create);
        }

        using (DepositItem wood = new() { ItemId = ItemCatalog.Wood, Quantity = 50 })
        {
            _ = await founder.RequestAsync<CoopResult>(wood);
        }

        using (DepositItem stone = new() { ItemId = ItemCatalog.Stone, Quantity = 25 })
        {
            _ = await founder.RequestAsync<CoopResult>(stone);
        }

        _ = await _rig.ProbeAsync<CoopResult>(founder, "Coop", CoopOpCode.UpgradeCoop, "UpgradeCoop", "warehouse to level 1", () => new UpgradeCoop { Track = CoopUpgradeTrack.Warehouse, TargetLevel = 1 });
    }
}
