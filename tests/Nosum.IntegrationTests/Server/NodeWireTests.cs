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
using Nosum.Gameplay.Calendar;
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
public sealed partial class NodeWireTests : IClassFixture<PostgresFixture>
{
    internal static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(60);
    internal static readonly DateTimeOffset s_start = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero); // after the calendar epoch, so game days pass
    internal static readonly int s_axe = SlotOf(ItemCatalog.Axe);
    internal static readonly int s_pickaxe = SlotOf(ItemCatalog.Pickaxe);
    internal static readonly int s_scythe = SlotOf(ItemCatalog.Scythe);
    internal static readonly int s_can = SlotOf(ItemCatalog.WateringCan);
    internal static readonly int s_hoe = SlotOf(ItemCatalog.Hoe);
    internal static readonly int s_parsnip = SlotOf(ItemCatalog.ParsnipSeeds);

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

        // Game days pass with the crop watered each day (docs/adr/ADR-021 D7): the scythe harvests the mature crop (a mature crop is cut whatever is held).
        TimeSpan gameDay = TimeSpan.FromMilliseconds(GameCalendar.DefaultDayLengthMs);
        for (int day = 1; day < CropCatalog.All[0].TotalGrowthDays; day++)
        {
            clock.Advance(gameDay);
            await Task.Delay(330, host.Timeout);
            (await player.UseAsync(s_can, x, y, host.Timeout)).Action.Should().Be(FarmAction.Watered);
        }

        clock.Advance(gameDay);
        await Task.Delay(330, host.Timeout);
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
}
