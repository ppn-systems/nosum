// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Node.C2S;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Domain.Nodes;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Nodes;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Tree growth, initial map seeding, daily node regrowth, and seasonal forage rollover over real Nalix WebSocket sessions
/// against the real host and a real PostgreSQL database (docs/adr/ADR-021, ADR-025).
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class NodeRolloverWireTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;

    public NodeRolloverWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task TreeSeed_PlantedOnFreeGround_GrowsByGameDays_AndTheDayRolloverIsSafeToRepeat()
    {
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        JoinMapResult join = await NodeWireTests.SeededJoinAsync(_database, host, player, []);
        (int x, int y) = host.TileInReach(join, GameMaps.Farm, 0);
        (await player.GrantAsync(ItemCatalog.PineSeed, 1, host.Timeout)).Should().BeTrue();
        InventorySlotRow seeds = await player.Slots.WaitAsync(s => s.ItemId == ItemCatalog.PineSeed, host.Timeout);

        UseItemResult planted = await player.UseAsync(seeds.Slot, x, y, host.Timeout);

        planted.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.PlantedTree });
        NodeRow sprout = (await player.NodeChanges.WaitAsync(c => c.Upserted.Count == 1, host.Timeout)).Upserted[0];
        sprout.Should().BeEquivalentTo(new { X = (ushort)x, Y = (ushort)y, Kind = (byte)NodeKind.Tree, Variant = TreeVariant.Pine, Stage = TreeStage.Seed });
        (await player.Slots.WaitAsync(s => s.Slot == seeds.Slot, host.Timeout)).Quantity.Should().Be(0, "the seed was used");
        (await player.UseAsync(NodeWireTests.s_hoe, x, y, host.Timeout)).Status.Should().Be(FarmStatus.Blocked);

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
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        JoinMapResult join = await NodeWireTests.SeededJoinAsync(_database, host, player, [(NodeKind.Stone, 0)]);
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
        await using (NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database))
        {
            await using NodeWireTests.ToolClient player = await host.ConnectAsync();
            token = player.SessionToken;
            account = player.AccountId;
            _ = await player.JoinAsync(host.Timeout);

            NodeSnapshot snapshot = await player.GetNodesAsync(host.Timeout);

            first = snapshot.Nodes.Count;
            first.Should().BeInRange(80, NodeLimits.MaxPerInstance);
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
        await using (NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database))
        {
            await using NodeWireTests.ToolClient again = await host.ConnectAsync(token);
            _ = await again.JoinAsync(host.Timeout);
            (await again.GetNodesAsync(host.Timeout)).Nodes.Should().HaveCount(first);
        }

        // Every node cleared: the instance stays empty because it was seeded once.
        await using (NosumDbContext db = new(_database.Options))
        {
            _ = await db.WorldNodes.Where(n => n.OwnerId == account).ExecuteDeleteAsync();
        }

        await using (NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database))
        {
            await using NodeWireTests.ToolClient again = await host.ConnectAsync(token);
            _ = await again.JoinAsync(host.Timeout);
            (await again.GetNodesAsync(host.Timeout)).Nodes.Should().BeEmpty();
        }

        // Another player's farm is filled on its own, and with its own layout.
        await using (NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database))
        {
            await using NodeWireTests.ToolClient other = await host.ConnectAsync();
            _ = await other.JoinAsync(host.Timeout);
            (await other.GetNodesAsync(host.Timeout)).Nodes.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task DayRollover_OnAPublicMap_RegrowsABoundedNumberOfWeedsStonesAndTwigs_AndOnceADay()
    {
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, new FakeTimeProvider(NodeWireTests.s_start));
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.Forest, out MapDefinition? forest);
        await NodeWireTests.SeedAsync(_database, GameMaps.Forest, Guid.Empty);
        await player.PutOnAsync(GameMaps.Forest, (float)forest!.SpawnX, (float)forest.SpawnY);
        _ = await player.JoinAsync(host.Timeout);
        (await player.GetNodesAsync(host.Timeout)).Nodes.Should().BeEmpty();
        IResourceNodeService nodes = InstanceManager.Instance.GetExistingInstance<IResourceNodeService>()!;
        IGameDayClock days = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!;

        await nodes.AdvanceDayAsync(days.Today + 1, false);

        // A regrowth touches several chunks and leaves as one packet per chunk to the players who can see it (docs/adr/ADR-022 D4): the player gets the nodes in
        // sight, added up over the packets, and not the ones far away.
        ChunkCoord home = ChunkCoord.OfPixels(forest.SpawnX, forest.SpawnY, forest.Grid.TileSize);
        List<WorldNode> grown = await ForestNodesAsync();
        grown.Count.Should().BeInRange(1, NodeLimits.RegrowPerDay + NodeLimits.ForagePerDay);
        List<WorldNode> inSight = [.. grown.Where(n => ChunkCoord.OfTile(n.TileX, n.TileY).DistanceTo(home) <= 1)];
        List<NodeRow> grew = await CollectUpsertsAsync(player.NodeChanges, inSight.Count, host.Timeout);
        grew.Should().OnlyContain(n => n.Kind == (byte)NodeKind.Weed || n.Kind == (byte)NodeKind.Stone || n.Kind == (byte)NodeKind.Twig || n.Kind == (byte)NodeKind.Forage);
        grew.Select(n => n.Id).Should().BeEquivalentTo(inSight.Select(n => n.Id));

        await nodes.AdvanceDayAsync(days.Today + 1, false);
        await player.NodeChanges.ExpectNothingAsync(TimeSpan.FromMilliseconds(500));
        await nodes.AdvanceDayAsync(days.Today + 2, false);
        List<WorldNode> all = await ForestNodesAsync();
        (all.Count - grown.Count).Should().BeInRange(1, NodeLimits.RegrowPerDay + NodeLimits.ForagePerDay);
        HashSet<int> before = [.. grown.Select(n => n.Id)];
        int newInSight = all.Count(n => !before.Contains(n.Id) && ChunkCoord.OfTile(n.TileX, n.TileY).DistanceTo(home) <= 1);
        _ = await CollectUpsertsAsync(player.NodeChanges, newInSight, host.Timeout);
        (await player.GetNodesAsync(host.Timeout)).Nodes.Select(n => (n.X, n.Y)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task WoodsAndBeach_AreFilledFromTheirSites_TheStumpsBlockAndBreakInSixSwings_AndTheBeachRollsShoreForage()
    {
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.Woods, out MapDefinition? woods).Should().BeTrue();
        _ = maps.TryGet(GameMaps.Beach, out MapDefinition? beach).Should().BeTrue();
        woods!.HasNodes.Should().BeTrue();
        beach!.ForageSites.Should().NotBeEmpty().And.OnlyContain(s => s.Variant == ForagePools.Shore);

        // The first player on the Woods fills the whole map once: the six large stumps stand where ADR-037 puts them, beside the twigs, weeds and stones.
        await player.PutOnAsync(GameMaps.Woods, (float)woods.SpawnX, (float)woods.SpawnY);
        _ = await player.JoinAsync(host.Timeout);
        _ = await player.GetNodesAsync(host.Timeout);
        await using (NosumDbContext db = new(_database.Options))
        {
            List<WorldNode> rows = await db.WorldNodes.Where(n => n.MapId == GameMaps.Woods && n.OwnerId == Guid.Empty).ToListAsync();
            rows.Where(n => n.Kind == (short)NodeKind.LargeStump).Select(n => (n.TileX, n.TileY)).Should().BeEquivalentTo(OutdoorWorld.WoodsStumps.Select(s => (s.X, s.Y)));
            rows.Should().Contain(n => n.Kind == (short)NodeKind.Twig).And.Contain(n => n.Kind == (short)NodeKind.Weed);
            rows.Select(n => (n.TileX, n.TileY)).Should().OnlyHaveUniqueItems();
        }

        // A stump blocks the walk grid the server and the client share; the axe takes six swings and pays wood.
        NodeSite stumpSite = OutdoorWorld.WoodsStumps[3];
        woods.Grid.IsBlocked(stumpSite.X, stumpSite.Y).Should().BeFalse("the map itself is open there");
        NodeSnapshot snapshot = await player.GetNodesAsync(host.Timeout);
        snapshot.Nodes.Should().Contain(n => n.Kind == (byte)NodeKind.LargeStump && n.X == stumpSite.X && n.Y == stumpSite.Y && n.Hits == 6);
        IResourceNodeService nodes = InstanceManager.Instance.GetExistingInstance<IResourceNodeService>()!;
        nodes.GridFor(new MapInstance(GameMaps.Woods, Guid.Empty), woods).IsBlocked(stumpSite.X, stumpSite.Y).Should().BeTrue("a large stump blocks walking");

        // The Beach: nothing in its file is marked, so the scatters and the shore spots stand in; a day rollover rolls shells in the season of the day.
        IGameDayClock days = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!;
        await using NodeWireTests.ToolClient walker = await host.ConnectAsync();
        await walker.PutOnAsync(GameMaps.Beach, (float)beach.SpawnX, (float)beach.SpawnY);
        _ = await walker.JoinAsync(host.Timeout);
        _ = await walker.GetNodesAsync(host.Timeout);
        long day = days.Today + 1;
        await nodes.AdvanceDayAsync(day, false);

        await using NosumDbContext beachDb = new(_database.Options);
        List<WorldNode> beachRows = await beachDb.WorldNodes.Where(n => n.MapId == GameMaps.Beach && n.OwnerId == Guid.Empty).ToListAsync();
        beachRows.Should().Contain(n => n.Kind == (short)NodeKind.Twig).And.Contain(n => n.Kind == (short)NodeKind.Stone);
        List<WorldNode> shells = [.. beachRows.Where(n => n.Kind == (short)NodeKind.Forage)];
        shells.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(NodeLimits.ForagePerDay);
        Season season = ForageCatalog.SeasonOfDay(day);
        shells.Should().OnlyContain(n => ForageCatalog.All[n.Variant].Pool == ForagePools.Shore && ForageCatalog.IsInSeason((byte)n.Variant, season));
        shells.Select(n => (n.TileX, n.TileY)).Should().OnlyContain(t => !beach.Grid.IsBlocked(t.TileX, t.TileY));
    }

    [Fact]
    public async Task Forage_AppearsAtADayRollover_OnAPublicMap_InTheSeasonsItems_AndNeverOnAPrivateFarm()
    {
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.Forest, out MapDefinition? forest);
        await NodeWireTests.SeedAsync(_database, GameMaps.Forest, Guid.Empty);
        await player.PutOnAsync(GameMaps.Forest, (float)forest!.SpawnX, (float)forest.SpawnY);
        _ = await player.JoinAsync(host.Timeout);
        IResourceNodeService nodes = InstanceManager.Instance.GetExistingInstance<IResourceNodeService>()!;
        IGameDayClock days = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!;
        long day = days.Today + 1;

        await nodes.AdvanceDayAsync(day, false);

        // Forage is stored for the whole map; the player is sent only what lies in the chunks around them (docs/adr/ADR-022).
        List<WorldNode> forage = [.. (await ForestNodesAsync()).Where(n => n.Kind == (short)NodeKind.Forage)];
        forage.Should().HaveCountGreaterThan(0).And.HaveCountLessThanOrEqualTo(NodeLimits.ForagePerDay);
        Season season = ForageCatalog.SeasonOfDay(day);
        forage.Should().OnlyContain(n => ForageCatalog.IsInSeason((byte)n.Variant, season), "only what grows in the season of the day");
        forage.Select(n => (n.TileX, n.TileY)).Should().OnlyHaveUniqueItems();
        ChunkCoord home = ChunkCoord.OfPixels(forest.SpawnX, forest.SpawnY, forest.Grid.TileSize);
        int inSight = forage.Count(n => ChunkCoord.OfTile(n.TileX, n.TileY).DistanceTo(home) <= 1);

        // Forage never blocks walking, and a private farm gets none.
        (await player.GetNodesAsync(host.Timeout)).Nodes.Where(n => n.Kind == (byte)NodeKind.Forage).Should().HaveCount(inSight);
        await using NodeWireTests.ToolClient farmer = await host.ConnectAsync();
        _ = await NodeWireTests.SeededJoinAsync(_database, host, farmer, []);
        await nodes.AdvanceDayAsync(day + 1, true);
        (await farmer.GetNodesAsync(host.Timeout)).Nodes.Should().NotContain(n => n.Kind == (byte)NodeKind.Forage);
    }

    [Fact]
    public async Task Forage_AnEmptyHandAndAHeldItemBothPickItUp_AndItLandsInTheInventory_AndAnOldSeasonsForageWithersAtTheRollover()
    {
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.Forest, out MapDefinition? forest);
        (int[] tilesX, int[] tilesY) = ReachableTiles(host, GameMaps.Forest, 3);
        static byte Variant(int itemId) => (byte)ForageCatalog.All.ToList().FindIndex(e => e.ItemId == itemId);
        byte snowYam = Variant(ItemCatalog.SnowYam);
        await NodeWireTests.SeedAsync(
            _database,
            GameMaps.Forest,
            Guid.Empty,
            NodeWireTests.Row(NodeKind.Forage, tilesX[0], tilesY[0], Variant(ItemCatalog.Leek)),
            NodeWireTests.Row(NodeKind.Forage, tilesX[1], tilesY[1], Variant(ItemCatalog.Dandelion)),
            NodeWireTests.Row(NodeKind.Forage, tilesX[2], tilesY[2], snowYam));
        await player.PutOnAsync(GameMaps.Forest, (float)forest!.SpawnX, (float)forest.SpawnY);
        _ = await player.JoinAsync(host.Timeout);

        // A bare hand (hotbar slot 8 is empty): the leek is gone from the map and, once the drop is taken, in the bag.
        UseItemResult bare = await player.UseAsync(8, tilesX[0], tilesY[0], host.Timeout);
        bare.Status.Should().Be(FarmStatus.Ok);
        (await player.NodeChanges.WaitAsync(c => c.Removed.Count == 1, host.Timeout)).Removed.Should().HaveCount(1);
        (await player.Slots.WaitAsync(s => s.ItemId == ItemCatalog.Leek, host.Timeout)).Quantity.Should().Be(1);

        // A held item (parsnip seeds here; any item works): the dandelion.
        (await player.UseAsync(NodeWireTests.s_parsnip, tilesX[1], tilesY[1], host.Timeout)).Status.Should().Be(FarmStatus.Ok);
        (await player.Slots.WaitAsync(s => s.ItemId == ItemCatalog.Dandelion, host.Timeout)).Quantity.Should().Be(1);
        (await player.GetNodesAsync(host.Timeout)).Nodes.Should().NotContain(n => n.X == tilesX[1] && n.Y == tilesY[1], "it was picked up");

        // The snow yam of winter does not survive into spring: the day rollover takes it away and tells the instance.
        IResourceNodeService nodes = InstanceManager.Instance.GetExistingInstance<IResourceNodeService>()!;
        IGameDayClock days = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!;
        ForageCatalog.SeasonOfDay(days.Today + 1).Should().Be(Season.Spring, "the test server's clock is before the first summer");
        await nodes.AdvanceDayAsync(days.Today + 1, false);
        NodeChanged withered = await player.NodeChanges.WaitAsync(c => c.Removed.Count > 0, host.Timeout);
        withered.Removed.Should().HaveCount(1);
        await using NosumDbContext db = new(_database.Options);
        (await db.WorldNodes.AnyAsync(n => n.MapId == GameMaps.Forest && n.OwnerId == Guid.Empty && n.Kind == (short)NodeKind.Forage && n.Variant == snowYam)).Should().BeFalse();
    }

    [Fact]
    public async Task GetNodes_BeforeSignIn_IsNotAnswered()
    {
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database);
        using WebSocketSession plain = new(
            new TransportOptions { Address = "127.0.0.1", Port = host.Port },
            new WebSocketTransportOptions { Path = Program.WebSocketPath });
        await plain.ConnectAsync(ct: host.Timeout);
        await plain.HandshakeAsync(host.Timeout);
        using GetNodes request = new();

        Func<Task> act = async () => _ = await plain.RequestAsync<NodeSnapshot>(request, RequestOptions.Default.WithTimeout(1500), ct: host.Timeout);

        _ = await act.Should().ThrowAsync<Exception>();
    }

    private static (int[] X, int[] Y) ReachableTiles(NodeWireTests.TestHost host, string mapId, int count)
    {
        (int X, int Y)[] free = host.FreeTilesNear(mapId);
        return ([.. free.Take(count).Select(t => t.X)], [.. free.Take(count).Select(t => t.Y)]);
    }

    private async Task<List<WorldNode>> ForestNodesAsync()
    {
        await using NosumDbContext db = new(_database.Options);
        return await db.WorldNodes.Where(n => n.MapId == GameMaps.Forest && n.OwnerId == Guid.Empty).ToListAsync();
    }

    private static async Task<List<NodeRow>> CollectUpsertsAsync(NodeWireTests.Inbox<NodeChanged> changes, int expected, CancellationToken ct)
    {
        List<NodeRow> rows = [];
        while (rows.Count < expected)
        {
            rows.AddRange((await changes.WaitAsync(c => c.Upserted.Count > 0, ct)).Upserted);
        }

        rows.Should().HaveCount(expected);
        return rows;
    }
}
