// Copyright (c) 2026 PPN Systems. All rights reserved.

#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Placed pieces that cross a chunk border (docs/adr/ADR-022, #156) against a real PostgreSQL database: a piece is sent with every chunk it overlaps, shown to a
/// player who sees any of them, published to each of them, and sent again when one chunk leaves the interest set while another it overlaps stays.
/// </summary>
public sealed class PlacedChunkInterestTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;

    public PlacedChunkInterestTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Place_ATableAcrossAChunkBorder_IsPublishedToBothChunks()
    {
        RecordingPublisher publisher = new();
        using PlacementHarness world = new(_database, chunks: new InterestSet(), publisher: publisher);
        Guid player = await world.CreatePlayerAsync();
        (int x, int y) = OpenAcrossBorder(world);
        world.StandOnFarm(player, x + 2, y);
        int slot = await world.GiveAsync(player, ItemCatalog.OakTable, 1);

        PlaceOutcome outcome = await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), slot, x, y, 0);

        outcome.Status.Should().Be(PlaceStatus.Ok);
        publisher.Published.Should().BeEquivalentTo(new[] { ChunkCoord.OfTile(x, y), ChunkCoord.OfTile(x + 1, y) });
    }

    [Fact]
    public async Task FillAsync_TheChunkOfTheSecondHalf_IncludesThePieceAnchoredInTheFirst()
    {
        using PlacementHarness world = new(_database, chunks: new InterestSet(), publisher: new RecordingPublisher());
        Guid player = await world.CreatePlayerAsync();
        (int x, int y) = OpenAcrossBorder(world);
        world.StandOnFarm(player, x + 2, y);
        int slot = await world.GiveAsync(player, ItemCatalog.OakTable, 1);
        PlaceOutcome outcome = await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), slot, x, y, 0);

        using ChunkState state = new();
        await world.Placeables.FillAsync(new MapInstance(GameMaps.Farm, player), ChunkCoord.OfTile(x + 1, y), state);

        state.Placed.Should().ContainSingle(row => row.Id == outcome.ObjectId);
    }

    [Fact]
    public async Task GetAsync_APlayerWhoSeesOnlyTheSecondChunk_SeesThePiece()
    {
        InterestSet interest = new();
        using PlacementHarness world = new(_database, chunks: interest, publisher: new RecordingPublisher());
        Guid player = await world.CreatePlayerAsync();
        (int x, int y) = OpenAcrossBorder(world);
        world.StandOnFarm(player, x + 2, y);
        int slot = await world.GiveAsync(player, ItemCatalog.OakTable, 1);
        PlaceOutcome outcome = await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), slot, x, y, 0);
        interest.Interested.Add(ChunkCoord.OfTile(x + 1, y));
        PlayerPresence presence = PresenceOf(world, player);
        presence.Viewer = new ViewerState(new ChunkInterest(ChunkCoord.OfTile(x + 1, y), ChunkBounds.ForTiles(64, 64)));

        PlacedSnapshotView view = await world.Placeables.GetAsync(world.Presence.ConnectionOf(player));

        view.Objects.Should().ContainSingle(item => item.Id == outcome.ObjectId);
    }

    [Fact]
    public async Task LeftAsync_AChunkLeftWhileTheOtherHalfStaysInSight_SendsThePieceAgain()
    {
        InterestSet interest = new();
        using PlacementHarness world = new(_database, chunks: interest, publisher: new RecordingPublisher());
        Guid player = await world.CreatePlayerAsync();
        (int x, int y) = OpenAcrossBorder(world);
        world.StandOnFarm(player, x + 2, y);
        int slot = await world.GiveAsync(player, ItemCatalog.OakTable, 1);
        PlaceOutcome outcome = await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), slot, x, y, 0);
        interest.Interested.Add(ChunkCoord.OfTile(x + 1, y));

        await world.Placeables.LeftAsync(PresenceOf(world, player), ChunkCoord.OfTile(x, y));

        PlacedChanged correction = world.Presence.Sent<PlacedChanged>(player).Single();
        correction.Upserted.Should().ContainSingle(row => row.Id == outcome.ObjectId);
        correction.Removed.Should().BeEmpty();
    }

    [Fact]
    public async Task LeftAsync_TheLastChunkOfThePieceLeaves_RemovesIt()
    {
        InterestSet interest = new();
        using PlacementHarness world = new(_database, chunks: interest, publisher: new RecordingPublisher());
        Guid player = await world.CreatePlayerAsync();
        (int x, int y) = OpenAcrossBorder(world);
        world.StandOnFarm(player, x + 2, y);
        int slot = await world.GiveAsync(player, ItemCatalog.OakTable, 1);
        PlaceOutcome outcome = await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), slot, x, y, 0);

        await world.Placeables.LeftAsync(PresenceOf(world, player), ChunkCoord.OfTile(x + 1, y));

        PlacedChanged correction = world.Presence.Sent<PlacedChanged>(player).Single();
        correction.Removed.Should().ContainSingle().Which.Should().Be(outcome.ObjectId);
        correction.Upserted.Should().BeEmpty();
    }

    // The first tile of a 2 by 2 open patch whose left column is the last tile of a chunk, so its footprint crosses the chunk border.
    private static (int X, int Y) OpenAcrossBorder(PlacementHarness world)
    {
        MapDefinition map = world.Map(GameMaps.Farm);
        for (int y = 1; y + 1 < map.Grid.Height; y++)
        {
            for (int x = ChunkCoord.SizeTiles - 1; x + 1 < map.Grid.Width; x += ChunkCoord.SizeTiles)
            {
                if (IsOpen(map, x, y) && IsOpen(map, x + 1, y) && IsOpen(map, x, y + 1) && IsOpen(map, x + 1, y + 1))
                {
                    return (x, y);
                }
            }
        }

        throw new InvalidOperationException("the farm has no open 2 by 2 patch across a chunk border");
    }

    private static bool IsOpen(MapDefinition map, int x, int y) => !map.Grid.IsBlocked(x, y) && !map.ReservedTiles.Contains((x, y));

    private static PlayerPresence PresenceOf(PlacementHarness world, Guid player)
    {
        world.Presence.TryGet(world.Presence.ConnectionOf(player), out PlayerPresence? presence).Should().BeTrue();
        return presence!;
    }

    // The chunks a test says a player can see; the rest of the index is not needed here.
    private sealed class InterestSet : IChunkMap
    {
        public HashSet<ChunkCoord> Interested { get; } = [];

        public void Subscribe(PlayerPresence player, ChunkCoord center, ChunkBounds bounds)
        {
        }

        public bool Relocate(PlayerPresence player, ChunkCoord destination, Span<ChunkCoord> entered, out int enteredCount, Span<ChunkCoord> left, out int leftCount)
        {
            enteredCount = 0;
            leftCount = 0;
            return false;
        }

        public bool IsInterested(PlayerPresence player, ChunkCoord chunk) => this.Interested.Contains(chunk);

        public void CopySubscribers(MapInstance instance, ChunkCoord chunk, List<PlayerPresence> into)
        {
        }

        public void CopyVisiblePlayers(PlayerPresence viewer, List<PlayerPresence> into)
        {
        }
    }

    // Records the chunk of every chunk-wide publish; the packets are not needed for these assertions.
    private sealed class RecordingPublisher : IChunkPublisher
    {
        public List<ChunkCoord> Published { get; } = [];

        public ValueTask PublishAsync(MapInstance instance, ChunkCoord chunk, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default)
        {
            this.Published.Add(chunk);
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishInstanceAsync(MapInstance instance, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
