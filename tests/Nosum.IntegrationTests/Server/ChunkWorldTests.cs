// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Contracts.Farm;
using Nosum.Contracts.GroundItem.S2C;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Domain.Nodes;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Nodes;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Resource nodes and ground items under chunked interest management (docs/adr/ADR-022 D4, D5), over real Nalix WebSocket clients, the real host and a real
/// PostgreSQL database: a change far away is not received, and the chunk's nodes and items arrive with its state when the chunk joins the interest set.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class ChunkWorldTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(60);
    private static readonly int s_pickaxe = Array.FindIndex(StarterKit.Items, i => i.ItemId == ItemCatalog.Pickaxe);

    private readonly PostgresFixture _database;

    public ChunkWorldTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Stone_BrokenFarAway_IsNotReceived_ButBreakerAndTheirChunkStateSeeIt()
    {
        await using ChunkHost host = await ChunkHost.StartAsync(_database, s_timeout);
        _ = host.Maps.TryGet(GameMaps.Town, out MapDefinition? town);
        town!.HasNodes.Should().BeTrue("Town is a public place with resource nodes");
        await using ChunkClient a = await host.ConnectAsync();
        await using ChunkClient b = await host.ConnectAsync();
        var (first, second) = StandingPlaces.Pair(town, 4);

        // One stone beside B, nothing else on Town, so the map's random first fill does not interfere.
        int bx = (int)(second.X / town.Grid.TileSize);
        int by = (int)(second.Y / town.Grid.TileSize);
        (int stoneX, int stoneY) = new[] { (bx + 1, by), (bx - 1, by), (bx, by + 1), (bx, by - 1) }
            .First(t => !town.Grid.IsBlocked(t.Item1, t.Item2) && !town.ReservedTiles.Contains(t));
        int stoneId = await SeedStoneAsync(GameMaps.Town, stoneX, stoneY);

        await a.PutOnAsync(GameMaps.Town, first.X, first.Y);
        await b.PutOnAsync(GameMaps.Town, second.X, second.Y);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);

        // The stone arrives with the state of B's chunk; A, four chunks away, never hears of it.
        await b.WaitUntilAsync(() => b.ChunkStates.Any(c => c.Nodes.Any(n => n.Id == stoneId)), host.Timeout);
        await a.WaitForSnapshotsAsync(6, host.Timeout);
        a.ChunkStates.SelectMany(c => c.Nodes).Should().NotContain(n => n.Id == stoneId);
        (await a.GetNodesAsync(host.Timeout)).Nodes.Should().NotContain(n => n.Id == stoneId, "the reply covers the interest set only");
        (await b.GetNodesAsync(host.Timeout)).Nodes.Should().ContainSingle().Which.Id.Should().Be(stoneId);

        // B breaks it; the removal reaches B and not A.
        FarmStatus status = FarmStatus.Cooldown;
        for (int swing = 0; swing < 12 && !b.NodeChanges.Any(c => c.Removed.Contains(stoneId)); swing++)
        {
            await Task.Delay(400, host.Timeout);
            status = (await b.UseAsync(s_pickaxe, stoneX, stoneY, host.Timeout)).Status;
            status.Should().Be(FarmStatus.Ok);
        }

        await b.WaitUntilAsync(() => b.NodeChanges.Any(c => c.Removed.Contains(stoneId)), host.Timeout);
        await a.WaitForSnapshotsAsync(8, host.Timeout);
        a.NodeChanges.Should().BeEmpty("A stands four chunks from the stone");
    }

    [Fact]
    public async Task GroundItem_SpawnedInAFarChunk_IsNotReceived_ButArrivesWithTheChunkStateWhenWalkingIn()
    {
        await using ChunkHost host = await ChunkHost.StartAsync(_database, s_timeout);
        _ = host.Maps.TryGet(GameMaps.Farm, out MapDefinition? farm);
        await using ChunkClient a = await host.ConnectAsync();

        // The player stands at the south edge of chunk (4, 0) of their own farm; the item lies in chunk (4, 2), two chunks south.
        int column = StandingPlaces.ClearColumn(farm!, 4, 13, 18);
        double x = (column + 0.5) * 16;
        double startY = (13 + 0.5) * 16;
        double endY = (17 + 0.5) * 16;
        int itemX = ((4 * 16) + 5) * 16;
        int itemY = ((2 * 16) + 5) * 16;
        await a.PutOnAsync(GameMaps.Farm, x, startY);
        _ = await a.JoinAsync(host.Timeout);
        await a.WaitUntilAsync(() => a.ChunkStates.Count > 0 || a.SnapshotCount > 3, host.Timeout);

        IGroundItems groundItems = InstanceManager.Instance.GetExistingInstance<IGroundItems>()!;
        int dropped = await groundItems.DropAsync(new GroundDrop(new MapInstance(GameMaps.Farm, a.AccountId), itemX, itemY, ItemCatalog.Wood, 1, null), host.Timeout);
        dropped.Should().Be(1);

        // Not in the interest set: no spawn push, not in the reply, not in any chunk state.
        await a.WaitForSnapshotsAsync(8, host.Timeout);
        a.GroundSpawns.Should().BeEmpty();
        (await a.GetGroundItemsAsync(host.Timeout)).Items.Should().BeEmpty();
        a.ChunkStates.SelectMany(c => c.GroundItems).Should().BeEmpty();

        // Walking south over the border into chunk (4, 1) puts chunk (4, 2) in the set: the item arrives with its state, once.
        await a.WalkToAsync(x, startY, x, endY, 10, host.Timeout);
        await a.WaitUntilAsync(() => a.ChunkStates.Any(c => c.ChunkX == 4 && c.ChunkY == 2), host.Timeout);
        await a.WaitForSnapshotsAsync(6, host.Timeout);
        ChunkState far = a.ChunkStates.Where(c => c.ChunkX == 4 && c.ChunkY == 2).Should().ContainSingle().Subject;
        GroundItemRow row = far.GroundItems.Should().ContainSingle().Subject;
        row.Should().BeEquivalentTo(new { ItemId = ItemCatalog.Wood, Quantity = 1, X = (ushort)itemX, Y = (ushort)itemY });
        (await a.GetGroundItemsAsync(host.Timeout)).Items.Should().ContainSingle().Which.Id.Should().Be(row.Id);
        a.GroundTakens.Should().BeEmpty("the player is still far from the item");
    }

    // Replaces the nodes of a public map with one stone and marks the instance as filled, so the first load keeps exactly that.
    private async Task<int> SeedStoneAsync(string mapId, int tileX, int tileY)
    {
        await using NosumDbContext db = new(_database.Options);
        _ = await db.WorldNodes.Where(n => n.MapId == mapId && n.OwnerId == Guid.Empty).ExecuteDeleteAsync();
        _ = await db.NodeInstances.Where(n => n.MapId == mapId && n.OwnerId == Guid.Empty).ExecuteDeleteAsync();
        WorldNode stone = new()
        {
            MapId = mapId,
            OwnerId = Guid.Empty,
            TileX = tileX,
            TileY = tileY,
            Kind = (short)NodeKind.Stone,
            Hits = NodeCatalog.HitsFor(NodeKind.Stone, 0),
            SpawnedAt = DateTimeOffset.UtcNow,
        };
        db.WorldNodes.Add(stone);
        db.NodeInstances.Add(new NodeInstanceState { MapId = mapId, OwnerId = Guid.Empty, SeededAt = DateTimeOffset.UtcNow, LastDay = long.MaxValue / 2 });
        _ = await db.SaveChangesAsync();
        return stone.Id;
    }
}
