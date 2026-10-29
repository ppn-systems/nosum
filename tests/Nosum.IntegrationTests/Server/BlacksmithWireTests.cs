// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nosum.Abstractions.Nodes;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Mine;
using Nosum.Contracts.Mine.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.S2C;
using Nosum.Domain.Blacksmith;
using Nosum.Domain.Farm;
using Nosum.Domain.Nodes;
using Nosum.Gameplay.Blacksmith;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Shops;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The blacksmith and tiered tools (docs/adr/ADR-026 D1–D2) over real Nalix WebSocket sessions against the real host and a real PostgreSQL database:
/// upgrading a tool with coins and bars held for two game days, surviving a server restart, buying coal and ore at the counter,
/// wide line tilling and watering with copper/iron tools, and tiered pickaxe damage against ore nodes.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class BlacksmithWireTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;

    public BlacksmithWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Blacksmith_Upgrade_TakesToolBarsAndCoins_HoldsTheToolTwoGameDays_ThenGivesTheCopperHoe_AndSurvivesARestart()
    {
        string token;
        long ready;
        long coinsBefore;
        Guid playerId;
        await using (MineWireTests.TestHost host = await MineWireTests.TestHost.StartAsync(_database))
        {
            await using MineWireTests.Client smith = await host.ConnectAsync();
            playerId = smith.AccountId;
            token = smith.SessionToken;
            await smith.PutOnAsync(GameMaps.Town, MineWireTests.s_counter.X, MineWireTests.s_counter.Y);
            (await smith.JoinAsync(host.Timeout)).MapId.Should().Be(GameMaps.Town);
            await MineWireTests.GrantAsync(smith.AccountId, ItemCatalog.CopperBar, 5, 2_000);
            coinsBefore = await MineWireTests.CoinsAsync(smith.AccountId);

            BlacksmithState upgrade = await smith.UpgradeAsync(ItemCatalog.Hoe, host.Timeout);

            upgrade.Status.Should().Be(BlacksmithStatus.Ok);
            upgrade.ToolItemId.Should().Be(ItemCatalog.CopperHoe);
            upgrade.ReadyDay.Should().Be(upgrade.Today + 2, "two game days");
            ready = upgrade.ReadyDay;
            (await MineWireTests.CountAsync(smith.AccountId, ItemCatalog.Hoe)).Should().Be(0, "the smith holds the old hoe");
            (await MineWireTests.CountAsync(smith.AccountId, ItemCatalog.CopperBar)).Should().Be(0);
            (await MineWireTests.CoinsAsync(smith.AccountId)).Should().Be(coinsBefore - 2_000);
            await using (NosumDbContext db = new(_database.Options))
            {
                ToolUpgrade row = await db.ToolUpgrades.SingleAsync(u => u.PlayerId == smith.AccountId);
                row.Should().BeEquivalentTo(new { ToolItemId = ItemCatalog.CopperHoe, ReadyDay = ready });
            }

            // One job at a time, and a refused request takes nothing.
            await MineWireTests.GrantAsync(smith.AccountId, ItemCatalog.CopperBar, 5, 2_000);
            long coinsWithSecond = await MineWireTests.CoinsAsync(smith.AccountId);
            (await smith.UpgradeAsync(ItemCatalog.Pickaxe, host.Timeout)).Status.Should().Be(BlacksmithStatus.Busy);
            (await MineWireTests.CountAsync(smith.AccountId, ItemCatalog.Pickaxe)).Should().Be(1);
            (await MineWireTests.CountAsync(smith.AccountId, ItemCatalog.CopperBar)).Should().Be(5);
            (await MineWireTests.CoinsAsync(smith.AccountId)).Should().Be(coinsWithSecond);

            // Not before the second day.
            (await smith.CollectAsync(host.Timeout)).Status.Should().Be(BlacksmithStatus.NotReady);
            (await smith.AdvanceClockAsync(1, host.Timeout)).Ok.Should().BeTrue();
            BlacksmithState dayOne = await smith.CollectAsync(host.Timeout);
            dayOne.Status.Should().Be(BlacksmithStatus.NotReady);
            dayOne.ToolItemId.Should().Be(ItemCatalog.CopperHoe);
            dayOne.Today.Should().Be(ready - 1);
        }

        // A restart: the job is in the database, not in memory.
        await using (MineWireTests.TestHost restarted = await MineWireTests.TestHost.StartAsync(_database))
        {
            await using MineWireTests.Client smith = await restarted.ConnectAsync(token);
            smith.AccountId.Should().Be(playerId);
            (await smith.JoinAsync(restarted.Timeout)).MapId.Should().Be(GameMaps.Town);
            (await MineWireTests.CountAsync(smith.AccountId, ItemCatalog.Hoe)).Should().Be(0, "the join does not hand out a free hoe while the upgrade holds that kind");

            BlacksmithState held = await smith.GetBlacksmithAsync(restarted.Timeout);
            held.Should().BeEquivalentTo(new { Status = BlacksmithStatus.Ok, ToolItemId = ItemCatalog.CopperHoe, ReadyDay = ready });
            (await smith.CollectAsync(restarted.Timeout)).Status.Should().Be(BlacksmithStatus.NotReady, "the new server's clock has not moved");

            (await smith.AdvanceClockAsync(2, restarted.Timeout)).Ok.Should().BeTrue();
            BlacksmithState collected = await smith.CollectAsync(restarted.Timeout);

            collected.Should().BeEquivalentTo(new { Status = BlacksmithStatus.Ok, ToolItemId = 0, ReadyDay = 0L });
            (await MineWireTests.CountAsync(smith.AccountId, ItemCatalog.CopperHoe)).Should().Be(1);
            (await smith.CollectAsync(restarted.Timeout)).Status.Should().Be(BlacksmithStatus.NothingToCollect);
            await using NosumDbContext db = new(_database.Options);
            (await db.ToolUpgrades.AnyAsync(u => u.PlayerId == smith.AccountId)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Blacksmith_Refusals_TakeNothing()
    {
        await using MineWireTests.TestHost host = await MineWireTests.TestHost.StartAsync(_database);
        await using MineWireTests.Client player = await host.ConnectAsync();
        await player.PutOnAsync(GameMaps.Farm, -1, -1);
        _ = await player.JoinAsync(host.Timeout);
        await MineWireTests.GrantAsync(player.AccountId, ItemCatalog.CopperBar, 5, 2_000);
        long coins = await MineWireTests.CoinsAsync(player.AccountId);

        // Away from the counter.
        (await player.UpgradeAsync(ItemCatalog.Hoe, host.Timeout)).Status.Should().Be(BlacksmithStatus.NotAtCounter);
        (await player.CollectAsync(host.Timeout)).Status.Should().Be(BlacksmithStatus.NotAtCounter);
        (await MineWireTests.CountAsync(player.AccountId, ItemCatalog.Hoe)).Should().Be(1);

        // At the counter.
        await using MineWireTests.Client atCounter = await host.ConnectAsync();
        await atCounter.PutOnAsync(GameMaps.Town, MineWireTests.s_counter.X, MineWireTests.s_counter.Y);
        _ = await atCounter.JoinAsync(host.Timeout);
        await MineWireTests.GrantAsync(atCounter.AccountId, ItemCatalog.CopperBar, 5, 0);
        (await atCounter.UpgradeAsync(ItemCatalog.Hoe, host.Timeout)).Status.Should().Be(BlacksmithStatus.NotEnoughCoins, "the bars are there, the coins are not");
        (await MineWireTests.CountAsync(atCounter.AccountId, ItemCatalog.Hoe)).Should().Be(1);
        (await MineWireTests.CountAsync(atCounter.AccountId, ItemCatalog.CopperBar)).Should().Be(5);

        await MineWireTests.GrantAsync(atCounter.AccountId, 0, 0, 2_000);
        _ = await atCounter.AdvanceClockAsync(1, host.Timeout);
        await using MineWireTests.Client noBars = await host.ConnectAsync();
        await noBars.PutOnAsync(GameMaps.Town, MineWireTests.s_counter.X, MineWireTests.s_counter.Y);
        _ = await noBars.JoinAsync(host.Timeout);
        await MineWireTests.GrantAsync(noBars.AccountId, 0, 0, 2_000);
        (await noBars.UpgradeAsync(ItemCatalog.Hoe, host.Timeout)).Status.Should().Be(BlacksmithStatus.NotEnoughBars);
        (await noBars.UpgradeAsync(ItemCatalog.CopperHoe, host.Timeout)).Status.Should().Be(BlacksmithStatus.NoTool, "the copper hoe is not carried");

        // Things that cannot be upgraded. A negative id never reaches the handler: the wire bound drops it (PacketBoundsTests, docs/adr/ADR-032).
        foreach (int itemId in new[] { ItemCatalog.Scythe, ItemCatalog.IronHoe, ItemCatalog.Wood, ItemCatalog.ParsnipSeeds, 0, 99_999 })
        {
            (await noBars.UpgradeAsync(itemId, host.Timeout)).Status.Should().Be(BlacksmithStatus.NotUpgradable, $"item {itemId}");
        }

        (await MineWireTests.CoinsAsync(noBars.AccountId)).Should().BeGreaterThanOrEqualTo(2_000);
        await using NosumDbContext db = new(_database.Options);
        (await db.ToolUpgrades.CountAsync(u => u.PlayerId == noBars.AccountId || u.PlayerId == player.AccountId)).Should().Be(0);
        coins.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Blacksmith_SellsCoalAndOre_OnlyAtTheCounter()
    {
        await using MineWireTests.TestHost host = await MineWireTests.TestHost.StartAsync(_database);
        await using MineWireTests.Client atCounter = await host.ConnectAsync();
        await atCounter.PutOnAsync(GameMaps.Town, MineWireTests.s_counter.X, MineWireTests.s_counter.Y);
        _ = await atCounter.JoinAsync(host.Timeout);
        await MineWireTests.GrantAsync(atCounter.AccountId, 0, 0, 1_000);
        long coins = await MineWireTests.CoinsAsync(atCounter.AccountId);

        ShopResult coal = await atCounter.BuyAsync(ShopCatalog.Blacksmith, ItemCatalog.Coal, 2, host.Timeout);
        ShopResult copper = await atCounter.BuyAsync(ShopCatalog.Blacksmith, ItemCatalog.CopperOre, 3, host.Timeout);
        ShopResult iron = await atCounter.BuyAsync(ShopCatalog.Blacksmith, ItemCatalog.IronOre, 1, host.Timeout);

        (coal.Status, coal.Spent).Should().Be((ShopStatus.Ok, 300L));
        (copper.Status, copper.Spent).Should().Be((ShopStatus.Ok, 225L));
        (iron.Status, iron.Spent).Should().Be((ShopStatus.Ok, 150L));
        (await MineWireTests.CoinsAsync(atCounter.AccountId)).Should().Be(coins - 675);
        (await MineWireTests.CountAsync(atCounter.AccountId, ItemCatalog.Coal)).Should().Be(2);
        (await atCounter.BuyAsync(ShopCatalog.Blacksmith, ItemCatalog.Wood, 1, host.Timeout)).Status.Should().Be(ShopStatus.NotSold);
        (await atCounter.BuyAsync(ShopCatalog.SeedShop, ItemCatalog.ParsnipSeeds, 1, host.Timeout)).Status.Should().Be(ShopStatus.NotAtShop, "the seed shop is across town");

        await using MineWireTests.Client elsewhere = await host.ConnectAsync();
        await elsewhere.PutOnAsync(GameMaps.Farm, -1, -1);
        _ = await elsewhere.JoinAsync(host.Timeout);
        await MineWireTests.GrantAsync(elsewhere.AccountId, 0, 0, 1_000);
        (await elsewhere.BuyAsync(ShopCatalog.Blacksmith, ItemCatalog.Coal, 1, host.Timeout)).Status.Should().Be(ShopStatus.NotAtShop);
    }

    [Fact]
    public async Task WideUse_AnUpgradedHoeAndCan_WorkALineOfTiles_TheServerChecksTheClaimAgainstTheItem()
    {
        await using MineWireTests.TestHost host = await MineWireTests.TestHost.StartAsync(_database);
        await using MineWireTests.Client farmer = await host.ConnectAsync();
        await farmer.PutOnAsync(GameMaps.Farm, -1, -1);

        // A farm with nothing on it: the map's random debris would block a tile of the line.
        await using (NosumDbContext seed = new(_database.Options))
        {
            seed.NodeInstances.Add(new NodeInstanceState { MapId = GameMaps.Farm, OwnerId = farmer.AccountId, SeededAt = MineWireTests.s_start, LastDay = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!.Today });
            _ = await seed.SaveChangesAsync();
        }

        JoinMapResult join = await farmer.JoinAsync(host.Timeout);
        join.Facing.Should().Be(0, "the farmer arrives facing down");
        (int x, int y) = host.LineStart(join, 5);
        int hoeSlot = await farmer.SlotOfAsync(ItemCatalog.Hoe);
        int canSlot = await farmer.SlotOfAsync(ItemCatalog.WateringCan);
        int axeSlot = await farmer.SlotOfAsync(ItemCatalog.Axe);

        // A basic hoe works one tile; the claim of a wider line is refused without touching the soil.
        (await farmer.UseAreaAsync(hoeSlot, x, y, 3, host.Timeout)).Status.Should().Be(FarmStatus.InvalidArea);
        (await farmer.UseAreaAsync(axeSlot, x, y, 3, host.Timeout)).Status.Should().Be(FarmStatus.InvalidArea, "only a hoe and a can work a line");
        (await farmer.UseAreaAsync(hoeSlot, x, y, 2, host.Timeout)).Status.Should().Be(FarmStatus.InvalidArea);
        (await farmer.UseAreaAsync(hoeSlot, x, y, 0, host.Timeout)).Status.Should().Be(FarmStatus.InvalidArea);
        (await TilledAsync(farmer.AccountId)).Should().BeEmpty();

        // The copper hoe: three tiles in the way the farmer faces, from the tile in front.
        await MineWireTests.SwapAsync(farmer.AccountId, ItemCatalog.Hoe, ItemCatalog.CopperHoe);
        (await farmer.UseAreaAsync(hoeSlot, x, y, 5, host.Timeout)).Status.Should().Be(FarmStatus.InvalidArea, "a copper hoe tops out at three");
        (await TilledAsync(farmer.AccountId)).Should().BeEmpty();
        UseItemResult three = await farmer.UseAreaAsync(hoeSlot, x, y, 3, host.Timeout);
        three.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Tilled });
        (await TilledAsync(farmer.AccountId)).Should().Equal((x, y), (x, y + 1), (x, y + 2));

        // The iron hoe: five tiles. The three already tilled are skipped, the two new ones are tilled.
        await MineWireTests.SwapAsync(farmer.AccountId, ItemCatalog.CopperHoe, ItemCatalog.IronHoe);
        UseItemResult five = await farmer.UseAreaAsync(hoeSlot, x, y, 5, host.Timeout);
        five.Status.Should().Be(FarmStatus.Ok);
        (await TilledAsync(farmer.AccountId)).Should().Equal((x, y), (x, y + 1), (x, y + 2), (x, y + 3), (x, y + 4));

        // The copper can waters three tiles, one unit of water each.
        await MineWireTests.SwapAsync(farmer.AccountId, ItemCatalog.WateringCan, ItemCatalog.CopperWateringCan);
        UseItemResult watered = await farmer.UseAreaAsync(canSlot, x, y, 3, host.Timeout);
        watered.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Watered });
        (await farmer.Tools.WaitAsync(t => t.WaterLeft == 37, host.Timeout)).WaterCapacity.Should().Be(40, "a can that was never refilled still has the old capacity");
        InstanceManager.Instance.GetExistingInstance<IToolState>()!.WaterLeft(farmer.AccountId).Should().Be(37);
        await using NosumDbContext db = new(_database.Options);
        (await db.FarmPlots.CountAsync(p => p.OwnerId == farmer.AccountId && p.WateredDay > 0)).Should().Be(3);
    }

    [Fact]
    public async Task Pickaxe_Tiers_CopperBreaksCopperOreInTwoSwings_BasicInThree_IronBreaksIronOreInTwo()
    {
        await using MineWireTests.TestHost host = await MineWireTests.TestHost.StartAsync(_database);

        // A basic pickaxe on floor 1: copper ore takes three swings.
        await using (MineWireTests.Client basic = await host.ConnectAsync())
        {
            (NodePlacement ore, _) = await MineWireTests.StandNextToAsync(host, basic, floor: 1, n => n.Kind == NodeKind.CopperOre);
            int slot = await basic.SlotOfAsync(ItemCatalog.Pickaxe);

            (await basic.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Hit);
            (await basic.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Hit);
            (await basic.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Broke);
        }

        // A copper pickaxe does two hits of damage: three hits of ore take two swings.
        await using (MineWireTests.Client copper = await host.ConnectAsync())
        {
            (NodePlacement ore, _) = await MineWireTests.StandNextToAsync(host, copper, floor: 1, n => n.Kind == NodeKind.CopperOre);
            await MineWireTests.SwapAsync(copper.AccountId, ItemCatalog.Pickaxe, ItemCatalog.CopperPickaxe);
            int slot = await copper.SlotOfAsync(ItemCatalog.CopperPickaxe);

            (await copper.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Hit);
            (await copper.NodeChanges.WaitAsync(c => c.Upserted.Any(n => n.X == ore.X && n.Y == ore.Y), host.Timeout)).Upserted.Single().Hits.Should().Be(1);
            (await copper.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Broke);
        }

        // Iron ore (four hits) lies on floor 20 and below; an iron pickaxe does three hits of damage.
        await using (MineWireTests.Client iron = await host.ConnectAsync())
        {
            (NodePlacement ore, _) = await MineWireTests.StandNextToAsync(host, iron, floor: 20, n => n.Kind == NodeKind.IronOre);
            await MineWireTests.SwapAsync(iron.AccountId, ItemCatalog.Pickaxe, ItemCatalog.IronPickaxe);
            int slot = await iron.SlotOfAsync(ItemCatalog.IronPickaxe);

            (await iron.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Hit);
            (await iron.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Broke);
            (await iron.Slots.WaitAsync(s => s.ItemId == ItemCatalog.IronOre, host.Timeout)).Quantity.Should().BeInRange(1, 3);
        }
    }

    private async Task<List<(int X, int Y)>> TilledAsync(Guid playerId)
    {
        await using NosumDbContext db = new(_database.Options);
        List<FarmPlot> plots = await db.FarmPlots.Where(p => p.OwnerId == playerId).ToListAsync();
        return [.. plots.OrderBy(p => p.Y).ThenBy(p => p.X).Select(p => (p.X, p.Y))];
    }
}
