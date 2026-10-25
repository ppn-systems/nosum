// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nosum.Abstractions.Calendar;
using Nosum.Abstractions.Shops;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Host;

namespace Nosum.IntegrationTests.Server.DbBudget;

public sealed partial class DbBudgetRun
{
    // The server's swing cooldown is 300 ms of real time.
    private static readonly TimeSpan s_swingGap = TimeSpan.FromMilliseconds(340);

    private async Task<UseItemResult> UseAsync(BudgetPlayer p, string variant, int slot, int x, int y, bool measure = true)
    {
        await Task.Delay(s_swingGap, _rig.Timeout);
        using UseItem request = new() { Slot = (byte)slot, TileX = x, TileY = y };
        return measure
            ? await _rig.MeasureAsync("Farm", FarmOpCode.UseItem, "UseItem", variant, () => p.RequestAsync<UseItemResult>(request))
            : await p.RequestAsync<UseItemResult>(request);
    }

    // Till, plant, water, wait out the growth, harvest: the farming loop that every player repeats all day.
    private async Task FarmAsync()
    {
        await using BudgetPlayer p = await _rig.SignedInGuestAsync();
        JoinMapResult join = await p.JoinAsync(Program.FarmMapId);
        (int x, int y) = TileInReachOf(join.SpawnX, join.SpawnY);
        await _rig.Counter.SettleAsync(TimeSpan.FromMilliseconds(300), _rig.Timeout);

        _ = await this.UseAsync(p, "plant on untilled soil (refused)", 2, x, y);
        _ = await this.UseAsync(p, "slot with nothing in it (refused)", 30, x, y);
        _ = await this.UseAsync(p, "till (hoe)", 0, x, y);
        _ = await this.UseAsync(p, "plant a seed", 2, x, y);
        _ = await this.UseAsync(p, "water", 1, x, y);

        // Further days: water each day without measuring, then the first measured water of a new day.
        IDevClockService clock = BudgetRig.Service<IDevClockService>();
        int days = CropCatalog.All[0].TotalGrowthDays;
        for (int day = 1; day <= days; day++)
        {
            _ = clock.AdvanceDays(1);
            await _rig.Counter.SettleAsync(TimeSpan.FromMilliseconds(250), _rig.Timeout);
            if (day < days)
            {
                _ = await this.UseAsync(p, "water on a new game day", 1, x, y, measure: day == 1);
            }
        }

        _ = await this.UseAsync(p, "harvest a mature crop", 8, x, y);

        // Eating: produce is food.
        IDevGrantService grants = BudgetRig.Service<IDevGrantService>();
        _ = await grants.GrantAsync(p.AccountId, ItemCatalog.Parsnip, 3, 0, _rig.Timeout);
        int slot = 9;
        using Eat eat = new() { Slot = (byte)slot };
        _ = await _rig.MeasureAsync("Player", PlayerOpCode.Eat, "Eat", "a parnsip from the hotbar", () => p.RequestAsync<EatResult>(eat));

        // The day rollover the server runs for everybody once per game day.
        _ = await _rig.MeasureAsync("Background", null, "DayClock", "one game day ends (nodes, goals)", () =>
        {
            _ = clock.AdvanceDays(1);
            return Task.FromResult(true);
        });
    }
}
