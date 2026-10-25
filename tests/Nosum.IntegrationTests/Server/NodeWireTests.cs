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
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Node.C2S;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Domain.Nodes;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Nodes;
using Nosum.Host;
using Nosum.Infrastructure.Nodes;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Tools and resource nodes (docs/adr/ADR-021) over real Nalix WebSocket sessions against the real host and a real PostgreSQL database: chopping a tree to a
/// stump, breaking a stone, cutting a weed, two players hitting one stone at once, nodes that block walking, a pickaxe on tilled soil, the watering can, tree seeds,
/// the once-only first fill and the day rollover. Each test puts the nodes it needs in the database before the player enters, so no test depends on the map's random fill.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class NodeWireTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(60);
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly int s_axe = SlotOf(ItemCatalog.Axe);
    private static readonly int s_pickaxe = SlotOf(ItemCatalog.Pickaxe);
    private static readonly int s_scythe = SlotOf(ItemCatalog.Scythe);
    private static readonly int s_can = SlotOf(ItemCatalog.WateringCan);
    private static readonly int s_hoe = SlotOf(ItemCatalog.Hoe);
    private static readonly int s_parsnip = SlotOf(ItemCatalog.ParsnipSeeds);

    private readonly PostgresFixture _database;

    public NodeWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Axe_ChoppingATree_TakesTenSwingsLeavesAStumpAndPaysWood_ThenTheStumpGoes()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using ToolClient player = await host.ConnectAsync();
        JoinMapResult join = await SeededJoinAsync(host, player, [(NodeKind.Tree, TreeVariant.Maple)]);
        (int treeX, int treeY) = host.TileInReach(join, GameMaps.Farm, 0);

        NodeSnapshot snapshot = await player.GetNodesAsync(host.Timeout);
        snapshot.MapId.Should().Be(GameMaps.Farm);
        snapshot.WaterCapacity.Should().Be(40);
        NodeRow tree = snapshot.Nodes.Should().ContainSingle().Subject;
        tree.Should().BeEquivalentTo(new { X = (ushort)treeX, Y = (ushort)treeY, Kind = (byte)NodeKind.Tree, Variant = TreeVariant.Maple, Stage = TreeStage.Grown, Hits = (byte)10 });

        // The wrong tool and the wrong thing do nothing.
        (await player.UseAsync(s_pickaxe, treeX, treeY, host.Timeout)).Status.Should().Be(FarmStatus.WrongTool);
        (await player.UseAsync(s_scythe, treeX, treeY, host.Timeout)).Status.Should().Be(FarmStatus.WrongTool);
        (await player.UseAsync(s_hoe, treeX, treeY, host.Timeout)).Status.Should().Be(FarmStatus.Blocked);

        // A second swing inside the cooldown is refused (docs/adr/ADR-021 D8); the first one counts.
        await Task.Delay(330, host.Timeout);
        UseItemResult first = await player.UseNowAsync(s_axe, treeX, treeY, host.Timeout);
        UseItemResult tooSoon = await player.UseNowAsync(s_axe, treeX, treeY, host.Timeout);
        first.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Hit });
        tooSoon.Status.Should().Be(FarmStatus.Cooldown);
        (await player.NodeChanges.WaitAsync(c => c.Upserted.Count == 1, host.Timeout)).Upserted[0].Hits.Should().Be(9);

        // Nine more swings: the tree takes ten in all, then it is a stump of the same species and the wood is in the bag.
        for (int swing = 2; swing <= 9; swing++)
        {
            (await player.UseAsync(s_axe, treeX, treeY, host.Timeout)).Action.Should().Be(FarmAction.Hit);
        }

        UseItemResult felled = await player.UseAsync(s_axe, treeX, treeY, host.Timeout);
        felled.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Broke });
        NodeChanged stump = await player.NodeChanges.WaitAsync(c => c.Upserted.Any(n => n.Kind == (byte)NodeKind.Stump), host.Timeout);
        stump.Upserted.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = tree.Id, Kind = (byte)NodeKind.Stump, Variant = TreeVariant.Maple, Hits = (byte)2 });
        InventorySlotRow wood = await player.Slots.WaitAsync(s => s.ItemId == ItemCatalog.Wood, host.Timeout);
        wood.Quantity.Should().BeInRange(6, 10);
        await using (NosumDbContext db = new(_database.Options))
        {
            WorldNode row = await db.WorldNodes.SingleAsync(n => n.Id == tree.Id);
            row.Should().BeEquivalentTo(new { Kind = (short)NodeKind.Stump, Variant = (short)TreeVariant.Maple, Hits = (short)2 });
        }

        // The stump: two more swings.
        (await player.UseAsync(s_axe, treeX, treeY, host.Timeout)).Action.Should().Be(FarmAction.Hit);
        (await player.UseAsync(s_axe, treeX, treeY, host.Timeout)).Action.Should().Be(FarmAction.Broke);
        (await player.NodeChanges.WaitAsync(c => c.Removed.Contains(tree.Id), host.Timeout)).Upserted.Should().BeEmpty();
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.WorldNodes.AnyAsync(n => n.Id == tree.Id)).Should().BeFalse();
        }

        (await player.GetNodesAsync(host.Timeout)).Nodes.Should().BeEmpty();
        IInventoryService inventory = InstanceManager.Instance.GetExistingInstance<IInventoryService>()!;
        (await inventory.GetAsync(player.AccountId)).Slots.Where(s => s.ItemId == ItemCatalog.Wood).Sum(s => s.Quantity).Should().BeInRange(8, 14, "six to ten from the tree, two to four from the stump");
    }

    [Fact]
    public async Task Pickaxe_BreakingAStone_RemovesItOnceAndPaysStone()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using ToolClient player = await host.ConnectAsync();
        JoinMapResult join = await SeededJoinAsync(host, player, [(NodeKind.Stone, 0)]);
        (int x, int y) = host.TileInReach(join, GameMaps.Farm, 0);

        (await player.UseAsync(s_axe, x, y, host.Timeout)).Status.Should().Be(FarmStatus.WrongTool);
        UseItemResult broke = await player.UseAsync(s_pickaxe, x, y, host.Timeout);

        broke.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Broke });
        NodeChanged removed = await player.NodeChanges.WaitAsync(c => c.Removed.Count == 1, host.Timeout);
        removed.Upserted.Should().BeEmpty();
        InventorySlotRow stone = await player.Slots.WaitAsync(s => s.ItemId == ItemCatalog.Stone, host.Timeout);
        stone.Quantity.Should().BeInRange(1, 2);
        await using NosumDbContext db = new(_database.Options);
        (await db.WorldNodes.AnyAsync(n => n.OwnerId == player.AccountId)).Should().BeFalse();

        // Nothing is left to hit: the farm layer answers (the tile is not tilled).
        (await player.UseAsync(s_pickaxe, x, y, host.Timeout)).Status.Should().Be(FarmStatus.NotTilled);
    }

    [Fact]
    public async Task Scythe_CuttingAWeed_PaysFiber_AndAWeedNeverBlocksWalking()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using ToolClient player = await host.ConnectAsync();
        JoinMapResult join = await SeededJoinAsync(host, player, [(NodeKind.Weed, 0)]);
        (int x, int y) = host.TileInReach(join, GameMaps.Farm, 0);

        // A weed does not stop the walker.
        await Task.Delay(700, host.Timeout);
        await player.SendMoveAsync(1, (x * 16) + 8, (y * 16) + 15, 0, host.Timeout);
        await player.Corrections.ExpectNothingAsync(TimeSpan.FromMilliseconds(600));
        (await player.UseAsync(s_hoe, x, y, host.Timeout)).Status.Should().Be(FarmStatus.Blocked, "the soil under a weed cannot be tilled");

        UseItemResult cut = await player.UseAsync(s_scythe, x, y, host.Timeout);

        cut.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Broke });
        (await player.Slots.WaitAsync(s => s.ItemId == ItemCatalog.Fiber, host.Timeout)).Quantity.Should().Be(1);
        (await player.NodeChanges.WaitAsync(c => c.Removed.Count == 1, host.Timeout)).Removed.Should().HaveCount(1);
    }

    [Fact]
    public async Task Walking_IntoAStoneIsRefusedByTheServer_AndAllowedOnceTheStoneIsBroken()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using ToolClient player = await host.ConnectAsync();
        JoinMapResult join = await SeededJoinAsync(host, player, [(NodeKind.Stone, 0)]);
        (int x, int y) = host.TileInReach(join, GameMaps.Farm, 0);
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.Farm, out MapDefinition? farm);
        farm!.Grid.IsBlocked(x, y).Should().BeFalse("the map itself lets a player stand there; only the node blocks");

        await Task.Delay(700, host.Timeout);
        await player.SendMoveAsync(1, (x * 16) + 8, (y * 16) + 15, 0, host.Timeout);
        (await player.Corrections.WaitAsync(_ => true, host.Timeout)).Reason.Should().Be(MoveRefusal.Blocked);

        (await player.UseAsync(s_pickaxe, x, y, host.Timeout)).Action.Should().Be(FarmAction.Broke);
        await Task.Delay(700, host.Timeout);
        await player.SendMoveAsync(2, (x * 16) + 8, (y * 16) + 15, 0, host.Timeout);
        await player.Corrections.ExpectNothingAsync(TimeSpan.FromMilliseconds(600));
    }

    [Fact]
    public async Task TwoPlayers_HittingOneStoneAtOnce_BreakItOnce_AndOnlyOneGetsTheYield()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using ToolClient a = await host.ConnectAsync();
        await using ToolClient b = await host.ConnectAsync();
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.Forest, out MapDefinition? forest);
        (int left, int middle, int right, int row) = OpenRow(forest!);
        await SeedAsync(GameMaps.Forest, Guid.Empty, Row(NodeKind.Stone, middle, row));
        await a.PutOnAsync(GameMaps.Forest, (left * 16) + 8, (row * 16) + 15);
        await b.PutOnAsync(GameMaps.Forest, (right * 16) + 8, (row * 16) + 15);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        (await a.GetNodesAsync(host.Timeout)).Nodes.Should().ContainSingle();

        UseItemResult[] results = await Task.WhenAll(
            a.UseNowAsync(s_pickaxe, middle, row, host.Timeout),
            b.UseNowAsync(s_pickaxe, middle, row, host.Timeout));

        results.Count(r => r.Status == FarmStatus.Ok && r.Action == FarmAction.Broke).Should().Be(1, "one swing breaks the stone");
        results.Count(r => r.Status == FarmStatus.Ok).Should().Be(1);
        results.Single(r => r.Status != FarmStatus.Ok).Status.Should().Be(FarmStatus.NothingToDo, "nothing is left to hit");
        IInventoryService inventory = InstanceManager.Instance.GetExistingInstance<IInventoryService>()!;
        int stoneA = (await inventory.GetAsync(a.AccountId)).Slots.Where(s => s.ItemId == ItemCatalog.Stone).Sum(s => s.Quantity);
        int stoneB = (await inventory.GetAsync(b.AccountId)).Slots.Where(s => s.ItemId == ItemCatalog.Stone).Sum(s => s.Quantity);
        (stoneA + stoneB).Should().BeInRange(1, 2, "one yield set, not two");
        (stoneA == 0 || stoneB == 0).Should().BeTrue("the yield went to one player");
        await using NosumDbContext db = new(_database.Options);
        (await db.WorldNodes.CountAsync(n => n.MapId == GameMaps.Forest && n.OwnerId == Guid.Empty)).Should().Be(0);
        (await a.NodeChanges.WaitAsync(c => c.Removed.Count == 1, host.Timeout)).Removed.Should().HaveCount(1);
        (await b.NodeChanges.WaitAsync(c => c.Removed.Count == 1, host.Timeout)).Removed.Should().HaveCount(1, "everyone on the instance is told, once");
    }

    [Fact]
    public async Task Pickaxe_ClearsEmptyTilledSoil_AndTheScytheHarvestsAMatureCrop_WhileAnEmptyCanRefusesToWater()
    {
        FakeTimeProvider clock = new(s_start);
        await using TestHost host = await TestHost.StartAsync(_database, clock);
        await using ToolClient player = await host.ConnectAsync();
        JoinMapResult join = await SeededJoinAsync(host, player, []);
        (int x, int y) = host.TillableTileInReach(join);

        // Till, then clear the empty soil with the pickaxe: the plot is gone for everyone and from the database.
        (await player.UseAsync(s_hoe, x, y, host.Timeout)).Action.Should().Be(FarmAction.Tilled);
        await player.TileChanges.WaitAsync(t => t.X == x && t.Y == y, host.Timeout);
        UseItemResult cleared = await player.UseAsync(s_pickaxe, x, y, host.Timeout);
        cleared.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Cleared });
        (await player.Cleared.WaitAsync(c => c.Tiles.Any(t => t.X == x && t.Y == y), host.Timeout)).MapId.Should().Be(GameMaps.Farm);
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.FarmPlots.AnyAsync(p => p.OwnerId == player.AccountId)).Should().BeFalse();
        }

        (await player.UseAsync(s_pickaxe, x, y, host.Timeout)).Status.Should().Be(FarmStatus.NotTilled);

        // A crop is not cleared away by the pickaxe; an empty can will not water it; a full one will.
        (await player.UseAsync(s_hoe, x, y, host.Timeout)).Action.Should().Be(FarmAction.Tilled);
        (await player.UseAsync(s_parsnip, x, y, host.Timeout)).Action.Should().Be(FarmAction.Planted);
        (await player.UseAsync(s_pickaxe, x, y, host.Timeout)).Status.Should().Be(FarmStatus.HasCrop);
        IToolState tools = InstanceManager.Instance.GetExistingInstance<IToolState>()!;
        for (int i = 0; i < NodeLimits.WateringCanCapacity; i++)
        {
            _ = await tools.UseWaterAsync(player.AccountId);
        }

        (await player.UseAsync(s_can, x, y, host.Timeout)).Status.Should().Be(FarmStatus.CanEmpty);
        _ = await tools.RefillAsync(player.AccountId);
        (await player.UseAsync(s_can, x, y, host.Timeout)).Action.Should().Be(FarmAction.Watered);
        (await player.Tools.WaitAsync(t => t.WaterLeft == 39, host.Timeout)).WaterCapacity.Should().Be(40);

        // The server clock moves on: the scythe harvests the mature crop (a mature crop is cut whatever is held).
        clock.Advance(TimeSpan.FromMilliseconds(CropCatalog.All[0].TotalGrowthMs + 1000));
        UseItemResult harvested = await player.UseAsync(s_scythe, x, y, host.Timeout);
        harvested.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Harvested });
        (await player.Slots.WaitAsync(s => s.ItemId == ItemCatalog.Parsnip, host.Timeout)).Quantity.Should().Be(1);
    }

    [Fact]
    public async Task WateringCan_IsRefilledAtWater_AndOnlyWhenItIsNotFull()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using ToolClient player = await host.ConnectAsync();
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.Farm, out MapDefinition? farm);
        (int waterX, int waterY, int standX, int standY) = ShoreOf(farm!);
        await SeedAsync(GameMaps.Farm, player.AccountId);
        await player.PutOnAsync(GameMaps.Farm, (standX * 16) + 8, (standY * 16) + 15);
        _ = await player.JoinAsync(host.Timeout);

        (await player.UseAsync(s_can, waterX, waterY, host.Timeout)).Status.Should().Be(FarmStatus.NothingToDo, "the can is full");
        IToolState tools = InstanceManager.Instance.GetExistingInstance<IToolState>()!;
        for (int i = 0; i < 25; i++)
        {
            _ = await tools.UseWaterAsync(player.AccountId);
        }

        (await player.GetNodesAsync(host.Timeout)).WaterLeft.Should().Be(15, "the snapshot carries the can");
        UseItemResult refilled = await player.UseAsync(s_can, waterX, waterY, host.Timeout);

        refilled.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Refilled });
        (await player.Tools.WaitAsync(t => t.WaterLeft == 40, host.Timeout)).WaterCapacity.Should().Be(40);
        (await player.UseAsync(s_axe, waterX, waterY, host.Timeout)).Status.Should().Be(FarmStatus.NothingToDo, "no node and no tilled soil there");
    }

    [Fact]
    public async Task TreeSeed_PlantedOnFreeGround_GrowsByGameDays_AndTheDayRolloverIsSafeToRepeat()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using ToolClient player = await host.ConnectAsync();
        JoinMapResult join = await SeededJoinAsync(host, player, []);
        (int x, int y) = host.TileInReach(join, GameMaps.Farm, 0);
        (await player.GrantAsync(ItemCatalog.PineSeed, 1, host.Timeout)).Should().BeTrue();
        InventorySlotRow seeds = await player.Slots.WaitAsync(s => s.ItemId == ItemCatalog.PineSeed, host.Timeout);

        UseItemResult planted = await player.UseAsync(seeds.Slot, x, y, host.Timeout);

        planted.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.PlantedTree });
        NodeRow sprout = (await player.NodeChanges.WaitAsync(c => c.Upserted.Count == 1, host.Timeout)).Upserted[0];
        sprout.Should().BeEquivalentTo(new { X = (ushort)x, Y = (ushort)y, Kind = (byte)NodeKind.Tree, Variant = TreeVariant.Pine, Stage = TreeStage.Seed });
        (await player.Slots.WaitAsync(s => s.Slot == seeds.Slot, host.Timeout)).Quantity.Should().Be(0, "the seed was used");
        (await player.UseAsync(s_hoe, x, y, host.Timeout)).Status.Should().Be(FarmStatus.Blocked);

        // Days pass: the seed becomes a sapling, a bush and a grown tree; asking for the same day twice changes nothing more.
        IResourceNodeService nodes = InstanceManager.Instance.GetExistingInstance<IResourceNodeService>()!;
        IGameDayClock days = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!;
        await nodes.AdvanceDayAsync(days.Today + (NodeLimits.DaysPerTreeStage * 3), false);
        NodeRow grown = (await player.NodeChanges.WaitAsync(c => c.Upserted.Any(n => n.Stage == TreeStage.Grown), host.Timeout)).Upserted.Single(n => n.Stage == TreeStage.Grown);
        grown.Should().BeEquivalentTo(new { Id = sprout.Id, Kind = (byte)NodeKind.Tree, Hits = (byte)10 });
        await nodes.AdvanceDayAsync(days.Today + (NodeLimits.DaysPerTreeStage * 3), false);
        await player.NodeChanges.ExpectNothingAsync(TimeSpan.FromMilliseconds(500));
        await using NosumDbContext db = new(_database.Options);
        (await db.WorldNodes.SingleAsync(n => n.Id == sprout.Id)).Should().BeEquivalentTo(new { Stage = (short)TreeStage.Grown, AgeDays = NodeLimits.DaysPerTreeStage * 3 });
    }

    [Fact]
    public async Task PlantingATreeSeed_InTheWrongPlace_IsRefused_AndKeepsTheSeed()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using ToolClient player = await host.ConnectAsync();
        JoinMapResult join = await SeededJoinAsync(host, player, [(NodeKind.Stone, 0)]);
        (int stoneX, int stoneY) = host.TileInReach(join, GameMaps.Farm, 0);
        (await player.GrantAsync(ItemCatalog.OakSeed, 2, host.Timeout)).Should().BeTrue();
        InventorySlotRow seeds = await player.Slots.WaitAsync(s => s.ItemId == ItemCatalog.OakSeed, host.Timeout);
        int feetX = (int)(join.SpawnX / 16);
        int feetY = (int)(join.SpawnY / 16);

        (await player.UseAsync(seeds.Slot, stoneX, stoneY, host.Timeout)).Status.Should().Be(FarmStatus.Blocked, "a stone stands there");
        (await player.UseAsync(seeds.Slot, feetX, feetY, host.Timeout)).Status.Should().Be(FarmStatus.Blocked, "the player stands there");
        (await player.UseAsync(seeds.Slot, 1, 1, host.Timeout)).Status.Should().Be(FarmStatus.OutOfReach);

        (await InstanceManager.Instance.GetExistingInstance<IInventoryService>()!.GetAsync(player.AccountId)).Slots.Single(s => s.ItemId == ItemCatalog.OakSeed).Quantity.Should().Be(2);
    }

    [Fact]
    public async Task NewInstance_IsFilledFromTheMapOnce_AndNeverRefilledAfterItsNodesAreGone()
    {
        string token;
        Guid account;
        int first;
        await using (TestHost host = await TestHost.StartAsync(_database))
        {
            await using ToolClient player = await host.ConnectAsync();
            token = player.SessionToken;
            account = player.AccountId;
            _ = await player.JoinAsync(host.Timeout);

            NodeSnapshot snapshot = await player.GetNodesAsync(host.Timeout);

            first = snapshot.Nodes.Count;
            first.Should().BeInRange(100, NodeLimits.MaxPerInstance);
            snapshot.Nodes.Should().Contain(n => n.Kind == (byte)NodeKind.Tree).And.Contain(n => n.Kind == (byte)NodeKind.Stone).And.Contain(n => n.Kind == (byte)NodeKind.Weed);
            snapshot.Nodes.Select(n => (n.X, n.Y)).Should().OnlyHaveUniqueItems();
            snapshot.Nodes.Where(n => n.Kind == (byte)NodeKind.Tree).Should().OnlyContain(n => n.Stage == TreeStage.Grown && n.Hits == 10);

            // The walkable spawn is never walled in: the arrival tiles carry no node.
            IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
            _ = maps.TryGet(GameMaps.Farm, out MapDefinition? farm);
            HashSet<(int, int)> reserved = [.. farm!.ReservedTiles];
            snapshot.Nodes.Where(n => reserved.Contains((n.X, n.Y))).Should().BeEmpty();
        }

        // The same instance on a restarted server: the nodes are the same ones, not a new draw.
        await using (TestHost host = await TestHost.StartAsync(_database))
        {
            await using ToolClient again = await host.ConnectAsync(token);
            _ = await again.JoinAsync(host.Timeout);
            (await again.GetNodesAsync(host.Timeout)).Nodes.Should().HaveCount(first);
        }

        // Every node cleared: the instance stays empty because it was seeded once.
        await using (NosumDbContext db = new(_database.Options))
        {
            _ = await db.WorldNodes.Where(n => n.OwnerId == account).ExecuteDeleteAsync();
        }

        await using (TestHost host = await TestHost.StartAsync(_database))
        {
            await using ToolClient again = await host.ConnectAsync(token);
            _ = await again.JoinAsync(host.Timeout);
            (await again.GetNodesAsync(host.Timeout)).Nodes.Should().BeEmpty();
        }

        // Another player's farm is filled on its own, and with its own layout.
        await using (TestHost host = await TestHost.StartAsync(_database))
        {
            await using ToolClient other = await host.ConnectAsync();
            _ = await other.JoinAsync(host.Timeout);
            (await other.GetNodesAsync(host.Timeout)).Nodes.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task DayRollover_OnAPublicMap_RegrowsABoundedNumberOfWeedsStonesAndTwigs_AndOnceADay()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using ToolClient player = await host.ConnectAsync();
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.Forest, out MapDefinition? forest);
        await SeedAsync(GameMaps.Forest, Guid.Empty);
        await player.PutOnAsync(GameMaps.Forest, (float)forest!.SpawnX, (float)forest.SpawnY);
        _ = await player.JoinAsync(host.Timeout);
        (await player.GetNodesAsync(host.Timeout)).Nodes.Should().BeEmpty();
        IResourceNodeService nodes = InstanceManager.Instance.GetExistingInstance<IResourceNodeService>()!;
        IGameDayClock days = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!;

        await nodes.AdvanceDayAsync(days.Today + 1, false);

        NodeChanged grew = await player.NodeChanges.WaitAsync(c => c.Upserted.Count > 0, host.Timeout);
        grew.Upserted.Should().HaveCountGreaterThan(0).And.HaveCountLessThanOrEqualTo(NodeLimits.RegrowPerDay);
        grew.Upserted.Should().OnlyContain(n => n.Kind == (byte)NodeKind.Weed || n.Kind == (byte)NodeKind.Stone || n.Kind == (byte)NodeKind.Twig);
        int count = grew.Upserted.Count;
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.WorldNodes.CountAsync(n => n.MapId == GameMaps.Forest && n.OwnerId == Guid.Empty)).Should().Be(count);
        }

        await nodes.AdvanceDayAsync(days.Today + 1, false);
        await player.NodeChanges.ExpectNothingAsync(TimeSpan.FromMilliseconds(500));
        await nodes.AdvanceDayAsync(days.Today + 2, false);
        NodeChanged more = await player.NodeChanges.WaitAsync(c => c.Upserted.Count > 0, host.Timeout);
        more.Upserted.Count.Should().BeLessThanOrEqualTo(NodeLimits.RegrowPerDay);
        (await player.GetNodesAsync(host.Timeout)).Nodes.Select(n => (n.X, n.Y)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetNodes_BeforeSignIn_IsNotAnswered()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        using WebSocketSession plain = new(
            new TransportOptions { Address = "127.0.0.1", Port = host.Port },
            new WebSocketTransportOptions { Path = Program.WebSocketPath });
        await plain.ConnectAsync(ct: host.Timeout);
        await plain.HandshakeAsync(host.Timeout);
        using GetNodes request = new();

        Func<Task> act = async () => _ = await plain.RequestAsync<NodeSnapshot>(request, RequestOptions.Default.WithTimeout(1500), ct: host.Timeout);

        _ = await act.Should().ThrowAsync<Exception>();
    }

    private static int SlotOf(int itemId) => Array.FindIndex(StarterKit.Items, i => i.ItemId == itemId);

    private static WorldNode Row(NodeKind kind, int x, int y, byte variant = 0, byte stage = TreeStage.Grown)
        => new()
        {
            TileX = x,
            TileY = y,
            Kind = (short)kind,
            Variant = variant,
            Stage = kind == NodeKind.Tree ? (short)stage : (short)0,
            Hits = NodeCatalog.HitsFor(kind, stage),
            SpawnedAt = s_start,
        };

    // Replaces the nodes of one instance with exactly these and marks it seeded, so the map's random first fill never happens.
    private async Task SeedAsync(string mapId, Guid owner, params WorldNode[] nodes)
    {
        await using NosumDbContext db = new(_database.Options);
        _ = await db.WorldNodes.Where(n => n.MapId == mapId && n.OwnerId == owner).ExecuteDeleteAsync();
        _ = await db.NodeInstances.Where(n => n.MapId == mapId && n.OwnerId == owner).ExecuteDeleteAsync();
        foreach (WorldNode node in nodes)
        {
            node.MapId = mapId;
            node.OwnerId = owner;
            db.WorldNodes.Add(node);
        }

        db.NodeInstances.Add(new NodeInstanceState { MapId = mapId, OwnerId = owner, SeededAt = s_start, LastDay = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!.Today });
        _ = await db.SaveChangesAsync();
    }

    // Seeds the farm of a new player with the given nodes (kind, species) at tiles that will be in reach of the spawn, then joins.
    private async Task<JoinMapResult> SeededJoinAsync(TestHost host, ToolClient player, (NodeKind Kind, byte Variant)[] nodes)
    {
        (int X, int Y)[] free = host.FreeTilesNear(GameMaps.Farm);
        WorldNode[] rows = [.. nodes.Select((n, i) => Row(n.Kind, free[i].X, free[i].Y, n.Variant))];
        await SeedAsync(GameMaps.Farm, player.AccountId, rows);
        return await player.JoinAsync(host.Timeout);
    }

    // Three open tiles in a row, with the middle one for a node: the tiles left and right are where two players stand.
    private static (int Left, int Middle, int Right, int Row) OpenRow(MapDefinition map)
    {
        int startX = (int)(map.SpawnX / 16);
        int startY = (int)(map.SpawnY / 16);
        for (int radius = 0; radius < 30; radius++)
        {
            for (int y = startY - radius; y <= startY + radius; y++)
            {
                for (int x = startX - radius; x <= startX + radius; x++)
                {
                    if (!map.Grid.IsBlocked(x - 1, y) && !map.Grid.IsBlocked(x, y) && !map.Grid.IsBlocked(x + 1, y) && !map.ReservedTiles.Contains((x, y)))
                    {
                        return (x - 1, x, x + 1, y);
                    }
                }
            }
        }

        throw new InvalidOperationException("No open row on " + map.Id);
    }

    // A water tile with a walkable tile beside it, and that walkable tile.
    private static (int WaterX, int WaterY, int StandX, int StandY) ShoreOf(MapDefinition map)
    {
        for (int y = 1; y < map.Grid.Height - 1; y++)
        {
            for (int x = 1; x < map.Grid.Width - 1; x++)
            {
                if (!map.Water!.IsWater(x, y))
                {
                    continue;
                }

                foreach ((int dx, int dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    if (!map.Grid.IsBlocked(x + dx, y + dy) && !map.Water.IsWater(x + dx, y + dy))
                    {
                        return (x, y, x + dx, y + dy);
                    }
                }
            }
        }

        throw new InvalidOperationException("No shore on " + map.Id);
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
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

        public static async Task<TestHost> StartAsync(PostgresFixture database, TimeProvider? clock = null)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);

            // Development, so the tester grant can hand out tree seeds.
            NetworkApplication app = Program.BuildApplication(port, clock, development: true);
            TestHost host = new(app, port);
            await app.ActivateAsync(host.Timeout);
            return host;
        }

        public async Task<ToolClient> ConnectAsync(string? sessionToken = null)
        {
            ToolClient client = new(new WebSocketSession(
                new TransportOptions { Address = "127.0.0.1", Port = this.Port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath }));
            await client.ConnectAsync(sessionToken, this.Timeout);
            return client;
        }

        // Walkable, unreserved tiles around the map's spawn, nearest first, that are within reach of the spawn feet.
        public (int X, int Y)[] FreeTilesNear(string mapId)
        {
            IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
            _ = maps.TryGet(mapId, out MapDefinition? map);
            int tx = (int)(map!.SpawnX / 16);
            int ty = (int)(map.SpawnY / 16);
            return [.. Enumerable.Range(-2, 5)
                .SelectMany(dx => Enumerable.Range(-2, 5).Select(dy => (X: tx + dx, Y: ty + dy)))
                .Where(t => !map.Grid.IsBlocked(t.X, t.Y) && !map.ReservedTiles.Contains((t.X, t.Y)) && !(t.X == tx && t.Y == ty) && FarmRules.InReach(map.SpawnX, map.SpawnY, t.X, t.Y, 16))
                .OrderBy(t => Math.Abs(t.X - tx) + Math.Abs(t.Y - ty))];
        }

        public (int X, int Y) TileInReach(JoinMapResult join, string mapId, int index)
        {
            (int X, int Y)[] free = this.FreeTilesNear(mapId);
            return free[index];
        }

        public (int X, int Y) TillableTileInReach(JoinMapResult join)
        {
            IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
            _ = maps.TryGet(GameMaps.Farm, out MapDefinition? farm);
            return this.FreeTilesNear(GameMaps.Farm).First(t => farm!.Tillable!.IsTillable(t.X, t.Y));
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
        private static DateTime s_last = DateTime.MinValue;

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

        // Nothing matching may arrive in the given time.
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

    private sealed class ToolClient : IAsyncDisposable
    {
        // The farm-use handler allows a burst and then eight a second per address; the server's swing cooldown is 300 ms.
        private static readonly TimeSpan s_swing = TimeSpan.FromMilliseconds(330);

        private readonly WebSocketSession _session;
        private readonly IDisposable[] _subscriptions;
        private DateTime _lastMove = DateTime.MinValue;

        public ToolClient(WebSocketSession session)
        {
            _session = session;
            _subscriptions =
            [
                session.On<NodeChanged>(p => this.NodeChanges.Add(new NodeChanged { MapId = p.MapId, Upserted = [.. p.Upserted.Select(Copy)], Removed = [.. p.Removed] })),
                session.On<ToolState>(p => this.Tools.Add(new ToolState { WaterLeft = p.WaterLeft, WaterCapacity = p.WaterCapacity })),
                session.On<FarmTilesCleared>(p => this.Cleared.Add(new FarmTilesCleared { MapId = p.MapId, Tiles = [.. p.Tiles.Select(t => new TileRow { X = t.X, Y = t.Y })] })),
                session.On<FarmTilesChanged>(p =>
                {
                    foreach (FarmTileRow t in p.Tiles)
                    {
                        this.TileChanges.Add(new FarmTileRow { X = t.X, Y = t.Y, CropId = t.CropId });
                    }
                }),
                session.On<InventoryUpdate>(p =>
                {
                    foreach (InventorySlotRow s in p.Slots)
                    {
                        this.Slots.Add(new InventorySlotRow { Slot = s.Slot, ItemId = s.ItemId, Quantity = s.Quantity });
                    }
                }),
                session.On<MoveCorrection>(c => this.Corrections.Add(new MoveCorrection { Sequence = c.Sequence, Reason = c.Reason, X = c.X, Y = c.Y })),
            ];
        }

        public Inbox<NodeChanged> NodeChanges { get; } = new();

        public Inbox<ToolState> Tools { get; } = new();

        public Inbox<FarmTilesCleared> Cleared { get; } = new();

        public Inbox<FarmTileRow> TileChanges { get; } = new();

        public Inbox<InventorySlotRow> Slots { get; } = new();

        public Inbox<MoveCorrection> Corrections { get; } = new();

        public Guid AccountId { get; private set; }

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

        // Saves where the player was last, so the server's join puts them there.
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
            return result;
        }

        public async Task<NodeSnapshot> GetNodesAsync(CancellationToken ct)
        {
            await Task.Delay(250, ct);
            using GetNodes request = new();
            return await _session.RequestAsync<NodeSnapshot>(request, ct: ct);
        }

        // Waits out the swing cooldown first, like a player who keeps swinging.
        public async Task<UseItemResult> UseAsync(int slot, int x, int y, CancellationToken ct)
        {
            await Task.Delay(s_swing, ct);
            return await this.UseNowAsync(slot, x, y, ct);
        }

        public async Task<UseItemResult> UseNowAsync(int slot, int x, int y, CancellationToken ct)
        {
            using UseItem request = new() { Slot = (byte)slot, TileX = x, TileY = y };
            return await _session.RequestAsync<UseItemResult>(request, ct: ct);
        }

        public async Task<bool> GrantAsync(int itemId, int quantity, CancellationToken ct)
        {
            using DevGrant request = new() { ItemId = itemId, Quantity = quantity };
            return (await _session.RequestAsync<DevGrantResult>(request, ct: ct)).Ok;
        }

        public async Task SendMoveAsync(uint sequence, float x, float y, byte facing, CancellationToken ct)
        {
            TimeSpan wait = TimeSpan.FromMilliseconds(120) - (DateTime.UtcNow - _lastMove);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            _lastMove = DateTime.UtcNow;
            using MoveIntent intent = new() { Sequence = sequence, X = x, Y = y, Facing = facing, Moving = true };
            await _session.SendAsync(intent, ct);
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
