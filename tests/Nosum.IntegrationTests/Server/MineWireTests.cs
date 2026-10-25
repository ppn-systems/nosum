// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Mine;
using Nosum.Contracts.Mine.C2S;
using Nosum.Contracts.Mine.S2C;
using Nosum.Contracts.Node.C2S;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Domain.Blacksmith;
using Nosum.Domain.Farm;
using Nosum.Domain.Mines;
using Nosum.Domain.Nodes;
using Nosum.Domain.World;
using Nosum.Gameplay.Blacksmith;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Shops;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Visits;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The blacksmith, the tiered tools and the mine (docs/adr/ADR-026) over real Nalix WebSocket sessions against the real host and a real PostgreSQL database:
/// an upgrade with coins and bars that the smith holds for two game days (the dev clock skips them) and that survives a restart, the wide use of an upgraded hoe
/// and can, the damage of a copper pickaxe, a stone that hides a ladder, the elevator that remembers the deepest floor, and a floor that is nobody else's.
/// Floors are built from the player, the game day and the floor, so each test computes the same plan the server does and stands the player beside a node of it.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class MineWireTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(90);
    private static readonly DateTimeOffset s_start = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero); // after the calendar epoch

    // The blacksmith's counter in Town: the doormat in front of the door, standing on its bottom edge like every feet position.
    private static readonly (float X, float Y) s_counter = ((MineWorld.BlacksmithCounter.X + 0.5f) * 16, ((MineWorld.BlacksmithCounter.Y + 1) * 16) - 1);

    private readonly PostgresFixture _database;

    public MineWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Blacksmith_Upgrade_TakesToolBarsAndCoins_HoldsTheToolTwoGameDays_ThenGivesTheCopperHoe_AndSurvivesARestart()
    {
        string token;
        long ready;
        long coinsBefore;
        Guid playerId;
        await using (TestHost host = await TestHost.StartAsync(_database))
        {
            await using Client smith = await host.ConnectAsync();
            playerId = smith.AccountId;
            token = smith.SessionToken;
            await smith.PutOnAsync(GameMaps.Town, s_counter.X, s_counter.Y);
            (await smith.JoinAsync(host.Timeout)).MapId.Should().Be(GameMaps.Town);
            await GrantAsync(smith.AccountId, ItemCatalog.CopperBar, 5, 2_000);
            coinsBefore = await CoinsAsync(smith.AccountId);

            BlacksmithState upgrade = await smith.UpgradeAsync(ItemCatalog.Hoe, host.Timeout);

            upgrade.Status.Should().Be(BlacksmithStatus.Ok);
            upgrade.ToolItemId.Should().Be(ItemCatalog.CopperHoe);
            upgrade.ReadyDay.Should().Be(upgrade.Today + 2, "two game days");
            ready = upgrade.ReadyDay;
            (await CountAsync(smith.AccountId, ItemCatalog.Hoe)).Should().Be(0, "the smith holds the old hoe");
            (await CountAsync(smith.AccountId, ItemCatalog.CopperBar)).Should().Be(0);
            (await CoinsAsync(smith.AccountId)).Should().Be(coinsBefore - 2_000);
            await using (NosumDbContext db = new(_database.Options))
            {
                ToolUpgrade row = await db.ToolUpgrades.SingleAsync(u => u.PlayerId == smith.AccountId);
                row.Should().BeEquivalentTo(new { ToolItemId = ItemCatalog.CopperHoe, ReadyDay = ready });
            }

            // One job at a time, and a refused request takes nothing.
            await GrantAsync(smith.AccountId, ItemCatalog.CopperBar, 5, 2_000);
            long coinsWithSecond = await CoinsAsync(smith.AccountId);
            (await smith.UpgradeAsync(ItemCatalog.Pickaxe, host.Timeout)).Status.Should().Be(BlacksmithStatus.Busy);
            (await CountAsync(smith.AccountId, ItemCatalog.Pickaxe)).Should().Be(1);
            (await CountAsync(smith.AccountId, ItemCatalog.CopperBar)).Should().Be(5);
            (await CoinsAsync(smith.AccountId)).Should().Be(coinsWithSecond);

            // Not before the second day.
            (await smith.CollectAsync(host.Timeout)).Status.Should().Be(BlacksmithStatus.NotReady);
            (await smith.AdvanceClockAsync(1, host.Timeout)).Ok.Should().BeTrue();
            BlacksmithState dayOne = await smith.CollectAsync(host.Timeout);
            dayOne.Status.Should().Be(BlacksmithStatus.NotReady);
            dayOne.ToolItemId.Should().Be(ItemCatalog.CopperHoe);
            dayOne.Today.Should().Be(ready - 1);
        }

        // A restart: the job is in the database, not in memory.
        await using (TestHost restarted = await TestHost.StartAsync(_database))
        {
            await using Client smith = await restarted.ConnectAsync(token);
            smith.AccountId.Should().Be(playerId);
            (await smith.JoinAsync(restarted.Timeout)).MapId.Should().Be(GameMaps.Town);

            BlacksmithState held = await smith.GetBlacksmithAsync(restarted.Timeout);
            held.Should().BeEquivalentTo(new { Status = BlacksmithStatus.Ok, ToolItemId = ItemCatalog.CopperHoe, ReadyDay = ready });
            (await smith.CollectAsync(restarted.Timeout)).Status.Should().Be(BlacksmithStatus.NotReady, "the new server's clock has not moved");

            (await smith.AdvanceClockAsync(2, restarted.Timeout)).Ok.Should().BeTrue();
            BlacksmithState collected = await smith.CollectAsync(restarted.Timeout);

            collected.Should().BeEquivalentTo(new { Status = BlacksmithStatus.Ok, ToolItemId = 0, ReadyDay = 0L });
            (await CountAsync(smith.AccountId, ItemCatalog.CopperHoe)).Should().Be(1);
            (await smith.CollectAsync(restarted.Timeout)).Status.Should().Be(BlacksmithStatus.NothingToCollect);
            await using NosumDbContext db = new(_database.Options);
            (await db.ToolUpgrades.AnyAsync(u => u.PlayerId == smith.AccountId)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Blacksmith_Refusals_TakeNothing()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client player = await host.ConnectAsync();
        await player.PutOnAsync(GameMaps.Farm, -1, -1);
        _ = await player.JoinAsync(host.Timeout);
        await GrantAsync(player.AccountId, ItemCatalog.CopperBar, 5, 2_000);
        long coins = await CoinsAsync(player.AccountId);

        // Away from the counter.
        (await player.UpgradeAsync(ItemCatalog.Hoe, host.Timeout)).Status.Should().Be(BlacksmithStatus.NotAtCounter);
        (await player.CollectAsync(host.Timeout)).Status.Should().Be(BlacksmithStatus.NotAtCounter);
        (await CountAsync(player.AccountId, ItemCatalog.Hoe)).Should().Be(1);

        // At the counter.
        await using Client atCounter = await host.ConnectAsync();
        await atCounter.PutOnAsync(GameMaps.Town, s_counter.X, s_counter.Y);
        _ = await atCounter.JoinAsync(host.Timeout);
        await GrantAsync(atCounter.AccountId, ItemCatalog.CopperBar, 5, 0);
        (await atCounter.UpgradeAsync(ItemCatalog.Hoe, host.Timeout)).Status.Should().Be(BlacksmithStatus.NotEnoughCoins, "the bars are there, the coins are not");
        (await CountAsync(atCounter.AccountId, ItemCatalog.Hoe)).Should().Be(1);
        (await CountAsync(atCounter.AccountId, ItemCatalog.CopperBar)).Should().Be(5);

        await GrantAsync(atCounter.AccountId, 0, 0, 2_000);
        _ = await atCounter.AdvanceClockAsync(1, host.Timeout);
        await using Client noBars = await host.ConnectAsync();
        await noBars.PutOnAsync(GameMaps.Town, s_counter.X, s_counter.Y);
        _ = await noBars.JoinAsync(host.Timeout);
        await GrantAsync(noBars.AccountId, 0, 0, 2_000);
        (await noBars.UpgradeAsync(ItemCatalog.Hoe, host.Timeout)).Status.Should().Be(BlacksmithStatus.NotEnoughBars);
        (await noBars.UpgradeAsync(ItemCatalog.CopperHoe, host.Timeout)).Status.Should().Be(BlacksmithStatus.NoTool, "the copper hoe is not carried");

        // Things that cannot be upgraded.
        foreach (int itemId in new[] { ItemCatalog.Scythe, ItemCatalog.IronHoe, ItemCatalog.Wood, ItemCatalog.ParsnipSeeds, 0, 99_999, -1 })
        {
            (await noBars.UpgradeAsync(itemId, host.Timeout)).Status.Should().Be(BlacksmithStatus.NotUpgradable, $"item {itemId}");
        }

        (await CoinsAsync(noBars.AccountId)).Should().BeGreaterThanOrEqualTo(2_000);
        await using NosumDbContext db = new(_database.Options);
        (await db.ToolUpgrades.CountAsync(u => u.PlayerId == noBars.AccountId || u.PlayerId == player.AccountId)).Should().Be(0);
        coins.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Blacksmith_SellsCoalAndOre_OnlyAtTheCounter()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client atCounter = await host.ConnectAsync();
        await atCounter.PutOnAsync(GameMaps.Town, s_counter.X, s_counter.Y);
        _ = await atCounter.JoinAsync(host.Timeout);
        await GrantAsync(atCounter.AccountId, 0, 0, 1_000);
        long coins = await CoinsAsync(atCounter.AccountId);

        ShopResult coal = await atCounter.BuyAsync(ShopCatalog.Blacksmith, ItemCatalog.Coal, 2, host.Timeout);
        ShopResult copper = await atCounter.BuyAsync(ShopCatalog.Blacksmith, ItemCatalog.CopperOre, 3, host.Timeout);
        ShopResult iron = await atCounter.BuyAsync(ShopCatalog.Blacksmith, ItemCatalog.IronOre, 1, host.Timeout);

        (coal.Status, coal.Spent).Should().Be((ShopStatus.Ok, 300L));
        (copper.Status, copper.Spent).Should().Be((ShopStatus.Ok, 225L));
        (iron.Status, iron.Spent).Should().Be((ShopStatus.Ok, 150L));
        (await CoinsAsync(atCounter.AccountId)).Should().Be(coins - 675);
        (await CountAsync(atCounter.AccountId, ItemCatalog.Coal)).Should().Be(2);
        (await atCounter.BuyAsync(ShopCatalog.Blacksmith, ItemCatalog.Wood, 1, host.Timeout)).Status.Should().Be(ShopStatus.NotSold);
        (await atCounter.BuyAsync(ShopCatalog.SeedShop, ItemCatalog.ParsnipSeeds, 1, host.Timeout)).Status.Should().Be(ShopStatus.NotAtShop, "the seed shop is across town");

        await using Client elsewhere = await host.ConnectAsync();
        await elsewhere.PutOnAsync(GameMaps.Farm, -1, -1);
        _ = await elsewhere.JoinAsync(host.Timeout);
        await GrantAsync(elsewhere.AccountId, 0, 0, 1_000);
        (await elsewhere.BuyAsync(ShopCatalog.Blacksmith, ItemCatalog.Coal, 1, host.Timeout)).Status.Should().Be(ShopStatus.NotAtShop);
    }

    [Fact]
    public async Task WideUse_AnUpgradedHoeAndCan_WorkALineOfTiles_TheServerChecksTheClaimAgainstTheItem()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client farmer = await host.ConnectAsync();
        await farmer.PutOnAsync(GameMaps.Farm, -1, -1);

        // A farm with nothing on it: the map's random debris would block a tile of the line.
        await using (NosumDbContext seed = new(_database.Options))
        {
            seed.NodeInstances.Add(new NodeInstanceState { MapId = GameMaps.Farm, OwnerId = farmer.AccountId, SeededAt = s_start, LastDay = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!.Today });
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
        await SwapAsync(farmer.AccountId, ItemCatalog.Hoe, ItemCatalog.CopperHoe);
        (await farmer.UseAreaAsync(hoeSlot, x, y, 5, host.Timeout)).Status.Should().Be(FarmStatus.InvalidArea, "a copper hoe tops out at three");
        (await TilledAsync(farmer.AccountId)).Should().BeEmpty();
        UseItemResult three = await farmer.UseAreaAsync(hoeSlot, x, y, 3, host.Timeout);
        three.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Tilled });
        (await TilledAsync(farmer.AccountId)).Should().Equal((x, y), (x, y + 1), (x, y + 2));

        // The iron hoe: five tiles. The three already tilled are skipped, the two new ones are tilled.
        await SwapAsync(farmer.AccountId, ItemCatalog.CopperHoe, ItemCatalog.IronHoe);
        UseItemResult five = await farmer.UseAreaAsync(hoeSlot, x, y, 5, host.Timeout);
        five.Status.Should().Be(FarmStatus.Ok);
        (await TilledAsync(farmer.AccountId)).Should().Equal((x, y), (x, y + 1), (x, y + 2), (x, y + 3), (x, y + 4));

        // The copper can waters three tiles, one unit of water each.
        await SwapAsync(farmer.AccountId, ItemCatalog.WateringCan, ItemCatalog.CopperWateringCan);
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
        await using TestHost host = await TestHost.StartAsync(_database);

        // A basic pickaxe on floor 1: copper ore takes three swings.
        await using (Client basic = await host.ConnectAsync())
        {
            (NodePlacement ore, _) = await StandNextToAsync(host, basic, floor: 1, n => n.Kind == NodeKind.CopperOre);
            int slot = await basic.SlotOfAsync(ItemCatalog.Pickaxe);

            (await basic.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Hit);
            (await basic.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Hit);
            (await basic.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Broke);
        }

        // A copper pickaxe does two hits of damage: three hits of ore take two swings.
        await using (Client copper = await host.ConnectAsync())
        {
            (NodePlacement ore, _) = await StandNextToAsync(host, copper, floor: 1, n => n.Kind == NodeKind.CopperOre);
            await SwapAsync(copper.AccountId, ItemCatalog.Pickaxe, ItemCatalog.CopperPickaxe);
            int slot = await copper.SlotOfAsync(ItemCatalog.CopperPickaxe);

            (await copper.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Hit);
            (await copper.NodeChanges.WaitAsync(c => c.Upserted.Any(n => n.X == ore.X && n.Y == ore.Y), host.Timeout)).Upserted.Single().Hits.Should().Be(1);
            (await copper.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Broke);
        }

        // Iron ore (four hits) lies on floor 20 and below; an iron pickaxe does three hits of damage.
        await using (Client iron = await host.ConnectAsync())
        {
            (NodePlacement ore, _) = await StandNextToAsync(host, iron, floor: 20, n => n.Kind == NodeKind.IronOre);
            await SwapAsync(iron.AccountId, ItemCatalog.Pickaxe, ItemCatalog.IronPickaxe);
            int slot = await iron.SlotOfAsync(ItemCatalog.IronPickaxe);

            (await iron.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Hit);
            (await iron.UseAsync(slot, ore.X, ore.Y, host.Timeout)).Action.Should().Be(FarmAction.Broke);
            (await iron.Slots.WaitAsync(s => s.ItemId == ItemCatalog.IronOre, host.Timeout)).Quantity.Should().BeInRange(1, 3);
        }
    }

    [Fact]
    public async Task Ladder_TheStoneHidesIt_BreakingItRevealsIt_AndUsingItTakesThePlayerDownAndRemembersTheDepth()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client miner = await host.ConnectAsync();
        (NodePlacement stone, MineFloorPlanner.Plan plan) = await StandNextToAsync(host, miner, floor: 1, n => n.Variant == NodeVariants.LadderStone);
        plan.Ladder.Should().Be((stone.X, stone.Y));

        // The client is never told which stone hides the ladder.
        NodeSnapshot snapshot = await miner.GetNodesAsync(host.Timeout);
        snapshot.MapId.Should().Be("mine1");
        snapshot.Nodes.Should().HaveCount(plan.Nodes.Count);
        snapshot.Nodes.Should().OnlyContain(n => n.Variant == 0 && n.Kind != (byte)NodeKind.Ladder);
        NodeRow hidden = snapshot.Nodes.Single(n => n.X == stone.X && n.Y == stone.Y);
        hidden.Kind.Should().Be((byte)NodeKind.Stone);

        // Facing a ladder that is not there yet does nothing; breaking the stone reveals it in the same place with the same id.
        int pickaxe = await miner.SlotOfAsync(ItemCatalog.Pickaxe);
        int hoe = await miner.SlotOfAsync(ItemCatalog.Hoe);
        (await miner.UseAsync(hoe, stone.X, stone.Y, host.Timeout)).Action.Should().NotBe(FarmAction.Descended);
        (await miner.UseAsync(pickaxe, stone.X, stone.Y, host.Timeout)).Action.Should().Be(FarmAction.Broke);
        NodeChanged revealed = await miner.NodeChanges.WaitAsync(c => c.Upserted.Any(n => n.Kind == (byte)NodeKind.Ladder), host.Timeout);
        revealed.Upserted.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = hidden.Id, X = hidden.X, Y = hidden.Y, Kind = (byte)NodeKind.Ladder });
        (await miner.Slots.WaitAsync(s => s.ItemId == ItemCatalog.Stone, host.Timeout)).Quantity.Should().BeGreaterThan(0, "the stone still gives stone");

        // Using the ladder (any item in hand) takes the player down to the next floor, and the deepest floor is written.
        UseItemResult descended = await miner.UseAsync(hoe, stone.X, stone.Y, host.Timeout);
        descended.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Descended });
        (await miner.MapChanges.WaitAsync(m => m.MapId == "mine2", host.Timeout)).MapId.Should().Be("mine2");
        (await miner.GetNodesAsync(host.Timeout)).MapId.Should().Be("mine2");
        MineState state = await miner.GetMineAsync(host.Timeout);
        state.Should().BeEquivalentTo(new { Status = MineStatus.Ok, Floor = 2, Deepest = 2 });
        await using NosumDbContext db = new(_database.Options);
        (await db.PlayerMines.SingleAsync(m => m.PlayerId == miner.AccountId)).DeepestFloor.Should().Be(2);
        (await db.WorldNodes.AnyAsync(n => n.MapId == "mine1" || n.MapId == "mine2")).Should().BeFalse("floors live in memory only");
        (await db.NodeInstances.AnyAsync(n => n.MapId == "mine1" || n.MapId == "mine2")).Should().BeFalse();
    }

    [Fact]
    public async Task Elevator_RemembersTheDeepestFloor_OffersEveryFifthFloorDownToIt_AndWorksOnlyAtAnElevator()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        string token;
        Guid playerId;
        await using (Client diver = await host.ConnectAsync())
        {
            token = diver.SessionToken;
            playerId = diver.AccountId;
            await diver.PutOnAsync("mine5", -1, -1);
            (await diver.JoinAsync(host.Timeout)).MapId.Should().Be("mine5");

            // Floor 5 has an elevator at its arrival tile; arriving there wrote the depth.
            (await diver.GetMineAsync(host.Timeout)).Should().BeEquivalentTo(new { Status = MineStatus.Ok, Floor = 5, Deepest = 5 });
            (await diver.RideAsync(5, host.Timeout)).Status.Should().Be(MineStatus.AlreadyThere);
            (await diver.RideAsync(10, host.Timeout)).Status.Should().Be(MineStatus.NotUnlocked, "never been that deep");
            (await diver.RideAsync(7, host.Timeout)).Status.Should().Be(MineStatus.NotUnlocked, "not an elevator floor");
            (await diver.RideAsync(-5, host.Timeout)).Status.Should().Be(MineStatus.NotUnlocked);
            MineState up = await diver.RideAsync(0, host.Timeout);
            up.Should().BeEquivalentTo(new { Status = MineStatus.Ok, Floor = 0, Deepest = 5 });
            (await diver.MapChanges.WaitAsync(m => m.MapId == GameMaps.Mine, host.Timeout)).MapId.Should().Be("mine");

            // The cave's spawn is across the cave from its elevator.
            (await diver.RideAsync(5, host.Timeout)).Status.Should().Be(MineStatus.NotAtElevator);
        }

        // Another day, another session: the depth is in the database, and the elevator is the one in the cave.
        await using (Client again = await host.ConnectAsync(token))
        {
            again.AccountId.Should().Be(playerId);
            await again.PutOnAsync(GameMaps.Mine, (MineWorld.CaveElevator.X + 0.5f) * 16, ((MineWorld.CaveElevator.Y + 2) * 16) - 1);
            (await again.JoinAsync(host.Timeout)).MapId.Should().Be(GameMaps.Mine);

            (await again.GetMineAsync(host.Timeout)).Should().BeEquivalentTo(new { Status = MineStatus.Ok, Floor = 0, Deepest = 5 });
            (await again.RideAsync(0, host.Timeout)).Status.Should().Be(MineStatus.AlreadyThere);
            (await again.RideAsync(10, host.Timeout)).Status.Should().Be(MineStatus.NotUnlocked);
            (await again.RideAsync(5, host.Timeout)).Should().BeEquivalentTo(new { Status = MineStatus.Ok, Floor = 5, Deepest = 5 });
            (await again.MapChanges.WaitAsync(m => m.MapId == "mine5", host.Timeout)).MapId.Should().Be("mine5");
            (await again.GetNodesAsync(host.Timeout)).MapId.Should().Be("mine5");
        }

        // A player who has been nowhere cannot ride anywhere.
        await using Client newcomer = await host.ConnectAsync();
        await newcomer.PutOnAsync(GameMaps.Mine, (MineWorld.CaveElevator.X + 0.5f) * 16, ((MineWorld.CaveElevator.Y + 2) * 16) - 1);
        _ = await newcomer.JoinAsync(host.Timeout);
        (await newcomer.GetMineAsync(host.Timeout)).Deepest.Should().Be(0);
        (await newcomer.RideAsync(5, host.Timeout)).Status.Should().Be(MineStatus.NotUnlocked);
        await using NosumDbContext db = new(_database.Options);
        (await db.PlayerMines.SingleAsync(m => m.PlayerId == playerId)).DeepestFloor.Should().Be(5);
    }

    [Fact]
    public async Task Floor_AnotherPlayersFloor_IsNotVisibleNotEnterableAndNotShared()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client ada = await host.ConnectAsync();
        await using Client bob = await host.ConnectAsync();
        (NodePlacement adaStone, MineFloorPlanner.Plan adaPlan) = await StandNextToAsync(host, ada, floor: 1, n => n.Kind == NodeKind.Stone && n.Variant == 0);
        _ = await StandNextToAsync(host, bob, floor: 1, n => n.Kind == NodeKind.Stone && n.Variant == 0);

        // Same map, two instances: different stones.
        NodeSnapshot adaNodes = await ada.GetNodesAsync(host.Timeout);
        NodeSnapshot bobNodes = await bob.GetNodesAsync(host.Timeout);
        adaNodes.MapId.Should().Be("mine1");
        bobNodes.MapId.Should().Be("mine1");
        adaNodes.Nodes.Select(n => (n.X, n.Y, n.Kind)).Should().BeEquivalentTo(adaPlan.Nodes.Select(n => ((ushort)n.X, (ushort)n.Y, (byte)n.Kind)));
        bobNodes.Nodes.Select(n => (n.X, n.Y, n.Kind)).Should().NotBeEquivalentTo(adaNodes.Nodes.Select(n => (n.X, n.Y, n.Kind)), "a floor is built from the player");

        // Neither sees the other on the map, and a stone ada breaks is not announced to bob.
        MapSnapshot seen = await bob.Snapshots.WaitAsync(_ => true, host.Timeout);
        seen.Players.Should().OnlyContain(p => p.PlayerId == bob.MapPlayerId);
        await ada.UseAsync(await ada.SlotOfAsync(ItemCatalog.Pickaxe), adaStone.X, adaStone.Y, host.Timeout);
        _ = await ada.NodeChanges.WaitAsync(c => c.Removed.Count > 0 || c.Upserted.Count > 0, host.Timeout);
        await bob.NodeChanges.ExpectNothingAsync(TimeSpan.FromMilliseconds(700));

        // The access rule: a floor is its owner's alone, even for somebody the owner let into their farm.
        AccessPolicy access = new(_database);
        (await access.CanEnterAsync(new MapInstance("mine1", ada.AccountId), ada.AccountId)).Should().BeTrue();
        (await access.CanEnterAsync(new MapInstance("mine1", ada.AccountId), bob.AccountId)).Should().BeFalse();
        (await access.CanEnterAsync(new MapInstance("mine1", bob.AccountId), ada.AccountId)).Should().BeFalse();
    }

    [Fact]
    public async Task Floor_ComesBackTheSameSeed_AndAnewTheNextGameDay_WithNothingLeftOnTheGround()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        string token;
        List<(ushort X, ushort Y, byte Kind)> first;
        Guid playerId;
        await using (Client miner = await host.ConnectAsync())
        {
            token = miner.SessionToken;
            playerId = miner.AccountId;
            (NodePlacement stone, MineFloorPlanner.Plan plan) = await StandNextToAsync(host, miner, floor: 1, n => n.Kind == NodeKind.Stone && n.Variant == 0);
            first = [.. (await miner.GetNodesAsync(host.Timeout)).Nodes.Select(n => (n.X, n.Y, n.Kind))];
            first.Should().HaveCount(plan.Nodes.Count);

            (await miner.UseAsync(await miner.SlotOfAsync(ItemCatalog.Pickaxe), stone.X, stone.Y, host.Timeout)).Action.Should().Be(FarmAction.Broke);
            (await miner.GetNodesAsync(host.Timeout)).Nodes.Should().HaveCount(plan.Nodes.Count - 1);
        }

        await host.WaitUntilNobodyIsOnAMapAsync();
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.GroundItems.CountAsync(g => g.MapId == "mine1")).Should().Be(0, "what lay on the floor went with it");
        }

        // The same day: the same floor, the broken stone is back.
        await using (Client same = await host.ConnectAsync(token))
        {
            same.AccountId.Should().Be(playerId);
            await same.PutOnAsync("mine1", -1, -1);
            _ = await same.JoinAsync(host.Timeout);
            (await same.GetNodesAsync(host.Timeout)).Nodes.Select(n => (n.X, n.Y, n.Kind)).Should().BeEquivalentTo(first);
        }

        await host.WaitUntilNobodyIsOnAMapAsync();

        // The next game day: another floor.
        await using (Client nextDay = await host.ConnectAsync(token))
        {
            (await nextDay.AdvanceClockAsync(1, host.Timeout)).Ok.Should().BeTrue();
            await nextDay.PutOnAsync("mine1", -1, -1);
            _ = await nextDay.JoinAsync(host.Timeout);
            (await nextDay.GetNodesAsync(host.Timeout)).Nodes.Select(n => (n.X, n.Y, n.Kind)).Should().NotBeEquivalentTo(first);
        }
    }

    // ---- helpers ----

    // Puts the player on a floor beside the first node of that floor's plan that matches (the server builds the same plan from the same inputs), and joins.
    private async Task<(NodePlacement Node, MineFloorPlanner.Plan Plan)> StandNextToAsync(TestHost host, Client player, int floor, Func<NodePlacement, bool> match)
    {
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(MineRules.FloorMapId(floor), out MapDefinition? map);
        long today = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!.Today;
        int spawnX = (int)(map!.SpawnX / 16);
        int spawnY = (int)((map.SpawnY - 1) / 16);
        MineFloorPlanner.Plan plan = MineFloorPlanner.Build(floor, MineRules.FloorSeed(player.AccountId, today, floor), map.Grid, map.ReservedTiles, spawnX, spawnY);
        HashSet<(int, int)> taken = [.. plan.Nodes.Select(n => (n.X, n.Y))];
        foreach (NodePlacement node in plan.Nodes.Where(match))
        {
            foreach ((int dx, int dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
            {
                int px = node.X + dx;
                int py = node.Y + dy;
                if (map.Grid.IsBlocked(px, py) || taken.Contains((px, py)) || map.ReservedTiles.Contains((px, py)))
                {
                    continue;
                }

                await player.PutOnAsync(map.Id, (px + 0.5f) * 16, ((py + 1) * 16) - 1);
                JoinMapResult join = await player.JoinAsync(host.Timeout);
                join.MapId.Should().Be(map.Id);
                join.SpawnX.Should().BeApproximately((px + 0.5f) * 16, 0.01f, "the player stands where the test put them, not at the arrival tile");
                return (node, plan);
            }
        }

        throw new InvalidOperationException("No node on floor " + floor + " has a free tile beside it.");
    }

    private static async Task GrantAsync(Guid playerId, int itemId, int quantity, long coins)
    {
        ITransferService transfers = InstanceManager.Instance.GetExistingInstance<ITransferService>()!;
        (await transfers.ExecuteAsync([new TransferLeg(TransferParty.Player(playerId), itemId, quantity, coins)], "test.grant")).Should().Be(TransferStatus.Ok);
    }

    private static Task<long> CoinsAsync(Guid playerId) => InstanceManager.Instance.GetExistingInstance<ITransferService>()!.GetBalanceAsync(playerId);

    private static async Task<int> CountAsync(Guid playerId, int itemId)
        => (await InstanceManager.Instance.GetExistingInstance<IInventoryService>()!.GetAsync(playerId)).Slots.Where(s => s.ItemId == itemId).Sum(s => s.Quantity);

    // Replaces one tool by another in the same slot, as the smith's counter does.
    private static async Task SwapAsync(Guid playerId, int oldItem, int newItem)
    {
        IInventoryService inventory = InstanceManager.Instance.GetExistingInstance<IInventoryService>()!;
        (await inventory.RemoveAsync(playerId, oldItem, 1, "test.swap")).IsOk.Should().BeTrue();
        (await inventory.AddAsync(playerId, newItem, 1, "test.swap")).IsOk.Should().BeTrue();
    }

    private async Task<List<(int X, int Y)>> TilledAsync(Guid playerId)
    {
        await using NosumDbContext db = new(_database.Options);
        List<FarmPlot> plots = await db.FarmPlots.Where(p => p.OwnerId == playerId).ToListAsync();
        return [.. plots.OrderBy(p => p.Y).ThenBy(p => p.X).Select(p => (p.X, p.Y))];
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    // The access policy of the host, built on the test database (the host keeps its own instance private).
    private sealed class AccessPolicy
    {
        private readonly VisitAccess _access;

        public AccessPolicy(PostgresFixture database)
        {
            _ = database;
            _access = new VisitAccess(InstanceManager.Instance.GetExistingInstance<Nosum.Abstractions.Data.IUnitOfWorkFactory>()!);
        }

        public async Task<bool> CanEnterAsync(MapInstance instance, Guid playerId) => await _access.CanEnterAsync(instance, playerId);
    }

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly NetworkApplication _app;
        private readonly CancellationTokenSource _timeout = new(s_timeout);

        private TestHost(NetworkApplication app, ushort port)
        {
            _app = app;
            this.Port = port;
        }

        public ushort Port { get; }

        public CancellationToken Timeout => _timeout.Token;

        public static async Task<TestHost> StartAsync(PostgresFixture database)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);

            // Development, so the tester clock can skip game days; the fake clock sits after the calendar epoch.
            NetworkApplication app = Program.BuildApplication(port, new FakeTimeProvider(s_start), development: true);
            TestHost host = new(app, port);
            await app.ActivateAsync(host.Timeout);
            return host;
        }

        public async Task<Client> ConnectAsync(string? sessionToken = null)
        {
            Client client = new(new WebSocketSession(
                new TransportOptions { Address = "127.0.0.1", Port = this.Port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath }));
            await client.ConnectAsync(sessionToken, this.Timeout);
            return client;
        }

        // The first tile of a vertical line of the given length on the farm, in reach of the spawn, that can be tilled all the way down.
        public (int X, int Y) LineStart(JoinMapResult join, int length)
        {
            IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
            _ = maps.TryGet(GameMaps.Farm, out MapDefinition? farm);
            int tx = (int)(join.SpawnX / 16);
            int ty = (int)(join.SpawnY / 16);
            foreach ((int x, int y) in Enumerable.Range(-2, 5).SelectMany(dx => Enumerable.Range(-2, 5).Select(dy => (X: tx + dx, Y: ty + dy))).OrderBy(t => Math.Abs(t.X - tx) + Math.Abs(t.Y - ty)))
            {
                bool lineOk = Enumerable.Range(0, length).All(i => farm!.Tillable!.IsTillable(x, y + i) && !farm.Grid.IsBlocked(x, y + i) && !farm.ReservedTiles.Contains((x, y + i)));
                if (lineOk && Nosum.Gameplay.Farm.FarmRules.InReach(join.SpawnX, join.SpawnY, x, y, 16) && !(x == tx && y == ty))
                {
                    return (x, y);
                }
            }

            throw new InvalidOperationException("No line of " + length + " tillable tiles near the farm's spawn.");
        }

        // A disconnect is noticed on the server's own time; floors are dropped when the last player has left them.
        public async Task WaitUntilNobodyIsOnAMapAsync()
        {
            Nosum.Abstractions.Presence.IMapPresence presence = InstanceManager.Instance.GetExistingInstance<Nosum.Abstractions.Presence.IMapPresence>()!;
            while (presence.PlayerCount > 0)
            {
                await Task.Delay(50, this.Timeout);
            }

            await Task.Delay(200, this.Timeout);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DeactivateAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _timeout.Dispose();
        }
    }

    private static class Pace
    {
        private static readonly SemaphoreSlim s_gate = new(1, 1);
        private static readonly SemaphoreSlim s_requestGate = new(1, 1);
        private static DateTime s_last = DateTime.MinValue;
        private static DateTime s_lastRequest = DateTime.MinValue;

        // The shop, blacksmith, mine and dev handlers allow a short burst and then a few requests a second per address, and every client of the suite shares one address.
        public static async Task RequestAsync(CancellationToken ct)
        {
            await s_requestGate.WaitAsync(ct);
            try
            {
                TimeSpan wait = TimeSpan.FromMilliseconds(330) - (DateTime.UtcNow - s_lastRequest);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }

                s_lastRequest = DateTime.UtcNow;
            }
            finally
            {
                _ = s_requestGate.Release();
            }
        }

        // Resuming a session is limited to two a second per address, and every test of the suite shares one.
        public static async Task WaitAsync(CancellationToken ct)
        {
            await s_gate.WaitAsync(ct);
            try
            {
                TimeSpan wait = TimeSpan.FromMilliseconds(650) - (DateTime.UtcNow - s_last);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }

                s_last = DateTime.UtcNow;
            }
            finally
            {
                _ = s_gate.Release();
            }
        }
    }

    // Pushes arrive on socket threads; tests read them in order and skip what does not match.
    private sealed class Inbox<T>
    {
        private readonly Channel<T> _items = Channel.CreateUnbounded<T>();

        public void Add(T item) => _ = _items.Writer.TryWrite(item);

        public async Task<T> WaitAsync(Func<T, bool> predicate, CancellationToken ct)
        {
            while (true)
            {
                T item = await _items.Reader.ReadAsync(ct);
                if (predicate(item))
                {
                    return item;
                }
            }
        }

        // Nothing may arrive in the given time.
        public async Task ExpectNothingAsync(TimeSpan time)
        {
            using CancellationTokenSource limit = new(time);
            try
            {
                T unexpected = await this.WaitAsync(_ => true, limit.Token);
                throw new InvalidOperationException("Unexpected " + unexpected!.GetType().Name);
            }
            catch (OperationCanceledException)
            {
                // Nothing arrived in time.
            }
        }
    }

    private sealed class Client : IAsyncDisposable
    {
        // The farm-use handler allows a burst and then eight a second per address; the server's swing cooldown is 300 ms.
        private static readonly TimeSpan s_swing = TimeSpan.FromMilliseconds(330);

        private readonly WebSocketSession _session;
        private readonly IDisposable[] _subscriptions;

        public Client(WebSocketSession session)
        {
            _session = session;
            _subscriptions =
            [
                session.On<NodeChanged>(p => this.NodeChanges.Add(new NodeChanged { MapId = p.MapId, Upserted = [.. p.Upserted.Select(Copy)], Removed = [.. p.Removed] })),
                session.On<ToolState>(p => this.Tools.Add(new ToolState { WaterLeft = p.WaterLeft, WaterCapacity = p.WaterCapacity })),
                session.On<MapChanged>(p => this.MapChanges.Add(new MapChanged { PlayerId = p.PlayerId, MapId = p.MapId, X = p.X, Y = p.Y, Facing = p.Facing })),
                session.On<MapSnapshot>(p => this.Snapshots.Add(new MapSnapshot { ServerTimeMs = p.ServerTimeMs, Players = [.. p.Players.Select(s => new PlayerState { PlayerId = s.PlayerId, X = s.X, Y = s.Y, Facing = s.Facing, Moving = s.Moving })] })),
                session.On<InventoryUpdate>(p =>
                {
                    foreach (InventorySlotRow s in p.Slots)
                    {
                        this.Slots.Add(new InventorySlotRow { Slot = s.Slot, ItemId = s.ItemId, Quantity = s.Quantity });
                    }
                }),
            ];
        }

        public Inbox<NodeChanged> NodeChanges { get; } = new();

        public Inbox<ToolState> Tools { get; } = new();

        public Inbox<MapChanged> MapChanges { get; } = new();

        public Inbox<MapSnapshot> Snapshots { get; } = new();

        public Inbox<InventorySlotRow> Slots { get; } = new();

        public Guid AccountId { get; private set; }

        public uint MapPlayerId { get; private set; }

        public string SessionToken { get; private set; } = string.Empty;

        public async Task ConnectAsync(string? sessionToken, CancellationToken ct)
        {
            await _session.ConnectAsync(ct: ct);
            await _session.HandshakeAsync(ct);
            if (sessionToken is null)
            {
                // The account is created in the database directly: sign-ups are rate limited per address.
                IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
                AccountOutcome created = await accounts.StartGuestAsync(Nosum.Contracts.Common.GameLanguage.English, ct);
                created.Status.Should().Be(AccountStatus.Ok);
                sessionToken = created.SessionToken!;
            }

            await Pace.WaitAsync(ct);
            using ResumeSession request = new() { SessionToken = sessionToken };
            AccountResult result = await _session.RequestAsync<AccountResult>(request, ct: ct);
            result.Status.Should().Be(AccountStatus.Ok);
            this.AccountId = result.PlayerId;
            this.SessionToken = sessionToken;
        }

        // Saves where the player was last, so the server's join puts them there (a negative x means the map's own spawn).
        public async Task PutOnAsync(string mapId, float x, float y)
        {
            IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
            await states.SaveAsync(this.AccountId, new PlayerStateRow(mapId, x, y, Nosum.Core.Facing.Down), CancellationToken.None);
        }

        public async Task<JoinMapResult> JoinAsync(CancellationToken ct)
        {
            using JoinMap request = new() { MapId = Program.FarmMapId };
            JoinMapResult result = await _session.RequestAsync<JoinMapResult>(request, ct: ct);
            result.Status.Should().Be(JoinStatus.Ok);
            this.MapPlayerId = result.PlayerId;
            return result;
        }

        public async Task<NodeSnapshot> GetNodesAsync(CancellationToken ct)
        {
            await Task.Delay(250, ct);
            using GetNodes request = new();
            return await _session.RequestAsync<NodeSnapshot>(request, ct: ct);
        }

        public async Task<int> SlotOfAsync(int itemId)
        {
            using GetInventory request = new();
            InventorySnapshot snapshot = await _session.RequestAsync<InventorySnapshot>(request, ct: CancellationToken.None);
            return snapshot.Slots.First(s => s.ItemId == itemId).Slot;
        }

        // Waits out the swing cooldown first, like a player who keeps swinging.
        public async Task<UseItemResult> UseAsync(int slot, int x, int y, CancellationToken ct)
        {
            await Task.Delay(s_swing, ct);
            using UseItem request = new() { Slot = (byte)slot, TileX = x, TileY = y };
            return await _session.RequestAsync<UseItemResult>(request, ct: ct);
        }

        public async Task<UseItemResult> UseAreaAsync(int slot, int x, int y, int tiles, CancellationToken ct)
        {
            await Task.Delay(s_swing, ct);
            using UseItemArea request = new() { Slot = (byte)slot, TileX = x, TileY = y, Tiles = (byte)tiles };
            return await _session.RequestAsync<UseItemResult>(request, ct: ct);
        }

        public async Task<BlacksmithState> GetBlacksmithAsync(CancellationToken ct)
        {
            await Pace.RequestAsync(ct);
            using GetBlacksmith request = new();
            return await _session.RequestAsync<BlacksmithState>(request, ct: ct);
        }

        public async Task<BlacksmithState> UpgradeAsync(int itemId, CancellationToken ct)
        {
            await Pace.RequestAsync(ct);
            using UpgradeTool request = new() { ItemId = itemId };
            return await _session.RequestAsync<BlacksmithState>(request, RequestOptions.Default.WithEncrypt(), ct: ct);
        }

        public async Task<BlacksmithState> CollectAsync(CancellationToken ct)
        {
            await Pace.RequestAsync(ct);
            using CollectTool request = new();
            return await _session.RequestAsync<BlacksmithState>(request, RequestOptions.Default.WithEncrypt(), ct: ct);
        }

        public async Task<ShopResult> BuyAsync(int shopId, int itemId, int quantity, CancellationToken ct)
        {
            await Pace.RequestAsync(ct);
            using BuyFromShop request = new() { ShopId = shopId, ItemId = itemId, Quantity = quantity };
            return await _session.RequestAsync<ShopResult>(request, RequestOptions.Default.WithEncrypt(), ct: ct);
        }

        public async Task<MineState> GetMineAsync(CancellationToken ct)
        {
            await Pace.RequestAsync(ct);
            using GetMine request = new();
            return await _session.RequestAsync<MineState>(request, ct: ct);
        }

        public async Task<MineState> RideAsync(int floor, CancellationToken ct)
        {
            await Pace.RequestAsync(ct);
            using RideElevator request = new() { Floor = floor };
            return await _session.RequestAsync<MineState>(request, ct: ct);
        }

        public async Task<DevAdvanceClockResult> AdvanceClockAsync(int days, CancellationToken ct)
        {
            await Pace.RequestAsync(ct);
            using DevAdvanceClock request = new() { Days = days };
            return await _session.RequestAsync<DevAdvanceClockResult>(request, RequestOptions.Default.WithEncrypt(), ct: ct);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            await _session.DisconnectAsync();
            _session.Dispose();
        }

        private static NodeRow Copy(NodeRow row) => new() { Id = row.Id, X = row.X, Y = row.Y, Kind = row.Kind, Variant = row.Variant, Stage = row.Stage, Hits = row.Hits };
    }
}
