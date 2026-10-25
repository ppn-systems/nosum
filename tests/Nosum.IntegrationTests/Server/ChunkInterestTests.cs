// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Framework.Injection;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Domain.Farm;
using Nosum.Gameplay.Maps;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Chunked interest management over real Nalix WebSocket clients, the real host and a real PostgreSQL database (docs/adr/ADR-022):
/// snapshots carry only the players in the receiver's interest set, spawns and despawns follow the chunks, and world events reach only the
/// players who can see the chunk they happened in.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class ChunkInterestTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(60);

    private readonly PostgresFixture _database;

    public ChunkInterestTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task TwoPlayers_FarApartOnTown_NeverReceiveEachOther()
    {
        await using ChunkHost host = await ChunkHost.StartAsync(_database, s_timeout);
        _ = host.Maps.TryGet(GameMaps.Town, out MapDefinition? town);
        await using ChunkClient a = await host.ConnectAsync();
        await using ChunkClient b = await host.ConnectAsync();
        var (first, second) = StandingPlaces.Pair(town!, 4);
        await a.PutOnAsync(GameMaps.Town, first.X, first.Y);
        await b.PutOnAsync(GameMaps.Town, second.X, second.Y);

        var joinA = await a.JoinAsync(host.Timeout);
        var joinB = await b.JoinAsync(host.Timeout);
        await a.WaitForSeenAsync(seen => seen.Contains(joinA.PlayerId), host.Timeout);
        await b.WaitForSeenAsync(seen => seen.Contains(joinB.PlayerId), host.Timeout);
        await a.WaitForSnapshotsAsync(8, host.Timeout);
        await b.WaitForSnapshotsAsync(8, host.Timeout);

        // One instance, two players: the server holds both, each snapshot holds one.
        IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()!;
        List<PlayerPresence> here = [];
        presence.CopyPlayers(GameMaps.Town, here);
        here.Should().HaveCount(2);
        a.LastSeen.Should().Equal(joinA.PlayerId);
        b.LastSeen.Should().Equal(joinB.PlayerId);
        a.Looks.Should().NotContain(l => l.PlayerId == joinB.PlayerId);
        b.Looks.Should().NotContain(l => l.PlayerId == joinA.PlayerId);
    }

    [Fact]
    public async Task TwoPlayers_InNeighbouringChunks_SeeEachOtherAndGetTheirLooks()
    {
        await using ChunkHost host = await ChunkHost.StartAsync(_database, s_timeout);
        _ = host.Maps.TryGet(GameMaps.Town, out MapDefinition? town);
        await using ChunkClient a = await host.ConnectAsync();
        await using ChunkClient b = await host.ConnectAsync();
        var (first, second) = StandingPlaces.Pair(town!, 1);
        await a.PutOnAsync(GameMaps.Town, first.X, first.Y);
        await b.PutOnAsync(GameMaps.Town, second.X, second.Y);

        var joinA = await a.JoinAsync(host.Timeout);
        var joinB = await b.JoinAsync(host.Timeout);

        await a.WaitForSeenAsync(seen => seen.Contains(joinA.PlayerId) && seen.Contains(joinB.PlayerId), host.Timeout);
        await b.WaitForSeenAsync(seen => seen.Contains(joinA.PlayerId) && seen.Contains(joinB.PlayerId), host.Timeout);
        await a.WaitUntilAsync(() => a.Looks.Any(l => l.PlayerId == joinB.PlayerId), host.Timeout);
        await b.WaitUntilAsync(() => b.Looks.Any(l => l.PlayerId == joinA.PlayerId), host.Timeout);
    }

    [Fact]
    public async Task Walking_IntoRangeSpawnsAndOutOfRangeDespawns_WithOneChunkOfHysteresis()
    {
        await using ChunkHost host = await ChunkHost.StartAsync(_database, s_timeout);
        _ = host.Maps.TryGet(GameMaps.Town, out MapDefinition? town);
        CrossingPlan plan = ChunkPlaces.FindCrossing(town!);
        await using ChunkClient a = await host.ConnectAsync();
        await using ChunkClient b = await host.ConnectAsync();
        await a.PutOnAsync(GameMaps.Town, plan.AX, plan.AY);
        await b.PutOnAsync(GameMaps.Town, plan.BX, plan.BY);
        var joinA = await a.JoinAsync(host.Timeout);
        var joinB = await b.JoinAsync(host.Timeout);

        // Two chunks apart: nobody sees anybody.
        await a.WaitForSeenAsync(seen => seen.Contains(joinA.PlayerId), host.Timeout);
        await b.WaitForSeenAsync(seen => seen.Contains(joinB.PlayerId), host.Timeout);
        await a.WaitForSnapshotsAsync(5, host.Timeout);
        a.LastSeen.Should().Equal(joinA.PlayerId);
        b.LastSeen.Should().Equal(joinB.PlayerId);

        // B steps over the border into chunk c0, next to A's neighbour: each is in the other's 3 x 3 block.
        int border = (plan.C0 + 1) * ChunkCoord.SizeTiles;
        await b.WalkToAsync(plan.BX, plan.BY, plan.ColumnX(border - 2), plan.BY, 10, host.Timeout);
        await a.WaitForSeenAsync(seen => seen.Contains(joinB.PlayerId), host.Timeout);
        await b.WaitForSeenAsync(seen => seen.Contains(joinA.PlayerId), host.Timeout);
        await a.WaitUntilAsync(() => a.Looks.Any(l => l.PlayerId == joinB.PlayerId), host.Timeout);
        await b.WaitUntilAsync(() => b.Looks.Any(l => l.PlayerId == joinA.PlayerId), host.Timeout);

        // B steps back east over the border. A's block is unchanged, so A loses B at once; B's block keeps A for one chunk (hysteresis).
        await b.WalkToAsync(plan.ColumnX(border - 2), plan.BY, plan.BX, plan.BY, 100, host.Timeout);
        await a.WaitForSeenAsync(seen => !seen.Contains(joinB.PlayerId), host.Timeout);
        await a.WaitUntilAsync(() => a.Left.Contains(joinB.PlayerId), host.Timeout);
        await b.WaitForSnapshotsAsync(6, host.Timeout);
        b.LastSeen.Should().Contain(joinA.PlayerId, "A is two chunks away, which B's interest set still holds");

        // B walks on east into chunk c0 + 2: A is now three chunks away and B's set lets go.
        int far = (plan.C0 + 2) * ChunkCoord.SizeTiles;
        await b.WalkToAsync(plan.BX, plan.BY, plan.ColumnX(far + 2), plan.BY, 200, host.Timeout);
        await b.WaitForSeenAsync(seen => !seen.Contains(joinA.PlayerId), host.Timeout);
        await b.WaitUntilAsync(() => b.Left.Contains(joinA.PlayerId), host.Timeout);
        await b.WaitUntilAsync(() => b.ChunkLefts.Count > 0, host.Timeout);
    }

    [Fact]
    public async Task Publish_ToAFarChunk_ReachesOnlyThePlayersWhoCanSeeIt()
    {
        await using ChunkHost host = await ChunkHost.StartAsync(_database, s_timeout);
        _ = host.Maps.TryGet(GameMaps.Town, out MapDefinition? town);
        await using ChunkClient a = await host.ConnectAsync();
        await using ChunkClient b = await host.ConnectAsync();
        var (first, second) = StandingPlaces.Pair(town!, 4);
        await a.PutOnAsync(GameMaps.Town, first.X, first.Y);
        await b.PutOnAsync(GameMaps.Town, second.X, second.Y);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        await a.WaitForSnapshotsAsync(3, host.Timeout);
        await b.WaitForSnapshotsAsync(3, host.Timeout);
        IChunkPublisher publisher = InstanceManager.Instance.GetExistingInstance<IChunkPublisher>()!;
        MapInstance town1 = MapInstance.Shared(GameMaps.Town);

        // A farm change where B stands...
        using FarmTilesChanged far = new() { MapId = GameMaps.Town, ServerNowMs = 1, Tiles = [new FarmTileRow { X = (ushort)((second.Chunk.X * 16) + 3), Y = (ushort)((second.Chunk.Y * 16) + 3), CropId = 1 }] };
        await publisher.PublishAsync(town1, second.Chunk, far);
        await b.WaitUntilAsync(() => b.FarmChanges.Count == 1, host.Timeout);
        await a.WaitForSnapshotsAsync(6, host.Timeout);

        // ... is not received by A, and one where A stands is not received by B.
        a.FarmChanges.Should().BeEmpty();
        using FarmTilesChanged near = new() { MapId = GameMaps.Town, ServerNowMs = 2, Tiles = [new FarmTileRow { X = (ushort)((first.Chunk.X * 16) + 3), Y = (ushort)((first.Chunk.Y * 16) + 3), CropId = 1 }] };
        await publisher.PublishAsync(town1, first.Chunk, near);
        await a.WaitUntilAsync(() => a.FarmChanges.Count == 1, host.Timeout);
        await b.WaitForSnapshotsAsync(6, host.Timeout);
        b.FarmChanges.Should().ContainSingle();

        // An instance publish is for map-wide things: everybody gets it.
        using FarmTilesChanged everywhere = new() { MapId = GameMaps.Town, ServerNowMs = 3, Tiles = [] };
        await publisher.PublishInstanceAsync(town1, everywhere);
        await a.WaitUntilAsync(() => a.FarmChanges.Count == 2, host.Timeout);
        await b.WaitUntilAsync(() => b.FarmChanges.Count == 2, host.Timeout);
    }

    [Fact]
    public async Task Farm_ChunkStateOfAFarAwayChunkArrivesOnlyWhenTheChunkJoinsTheInterestSet()
    {
        await using ChunkHost host = await ChunkHost.StartAsync(_database, s_timeout);
        _ = host.Maps.TryGet(GameMaps.Farm, out MapDefinition? farm);
        await using ChunkClient a = await host.ConnectAsync();
        // The player stands at the south edge of chunk (4, 0) of their own farm. One tilled plot lies in their chunk, one in chunk (4, 2), two chunks south.
        int column = StandingPlaces.ClearColumn(farm!, 4, 13, 18);
        double x = (column + 0.5) * 16;
        double startY = (13 + 0.5) * 16;
        double endY = (17 + 0.5) * 16;
        int nearX = (4 * 16) + 1;
        int nearY = 1;
        int farX = (4 * 16) + 5;
        int farY = (2 * 16) + 5;
        await a.PutOnAsync(GameMaps.Farm, x, startY);
        await SeedPlotAsync(a.AccountId, GameMaps.Farm, nearX, nearY);
        await SeedPlotAsync(a.AccountId, GameMaps.Farm, farX, farY);

        _ = await a.JoinAsync(host.Timeout);

        // The chunk the player stands in arrives with the join; the far one does not.
        await a.WaitUntilAsync(() => a.ChunkStates.Any(c => c.ChunkX == 4 && c.ChunkY == 0), host.Timeout);
        await a.WaitForSnapshotsAsync(8, host.Timeout);
        a.ChunkStates.Should().NotContain(c => c.ChunkX == 4 && c.ChunkY == 2, "chunk (4, 2) is two chunks away and not in the interest set yet");
        ChunkState home = a.ChunkStates.First(c => c.ChunkX == 4 && c.ChunkY == 0);
        home.MapId.Should().Be(GameMaps.Farm);
        home.FarmTiles.Should().ContainSingle().Which.Should().Match<FarmTileRow>(t => t.X == nearX && t.Y == nearY);

        // Walking south over the border into chunk (4, 1) puts chunk (4, 2) in the set: its plot arrives, once.
        await a.WalkToAsync(x, startY, x, endY, 10, host.Timeout);
        await a.WaitUntilAsync(() => a.ChunkStates.Any(c => c.ChunkX == 4 && c.ChunkY == 2), host.Timeout);
        await a.WaitForSnapshotsAsync(6, host.Timeout);
        a.ChunkStates.Where(c => c.ChunkX == 4 && c.ChunkY == 2).Should().ContainSingle()
            .Which.FarmTiles.Should().ContainSingle().Which.Should().Match<FarmTileRow>(t => t.X == farX && t.Y == farY);
        a.ChunkStates.Where(c => c.ChunkX == 4 && c.ChunkY == 0).Should().ContainSingle("a chunk that stays in the set is not sent again");
    }

    private async Task SeedPlotAsync(Guid owner, string mapId, int x, int y)
    {
        IUnitOfWorkFactory factory = InstanceManager.Instance.GetExistingInstance<IUnitOfWorkFactory>()!;
        IUnitOfWork unit = factory.Create();
        await using (unit.ConfigureAwait(false))
        {
            unit.Repository<FarmPlot>().Add(new FarmPlot { MapId = mapId, OwnerId = owner, X = x, Y = y, CropId = 0, UpdatedAt = DateTimeOffset.UtcNow });
            _ = await unit.SaveChangesAsync(CancellationToken.None);
        }
    }
}
