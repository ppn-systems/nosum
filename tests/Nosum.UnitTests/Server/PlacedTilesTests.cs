// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Placeables;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Placeables;
using Nosum.Infrastructure.Placeables;
using Xunit;

namespace Nosum.UnitTests.Server;

/// <summary>The shared placed-objects index and the world that joins the placed layer with the node layer (docs/adr/ADR-024 D3, D4).</summary>
public sealed class PlacedTilesTests
{
    private static readonly Guid s_owner = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static MapInstance Farm => new(GameMaps.Farm, s_owner);

    [Fact]
    public void UnknownInstance_HasNothingPlaced()
    {
        PlacedTiles tiles = new();

        tiles.ItemsOf(Farm).Should().BeEmpty();
        tiles.CoveredTiles(Farm).Should().BeEmpty();
        tiles.IsCovered(Farm, 1, 1).Should().BeFalse();
        tiles.IsSprinklerWatered(Farm, 1, 1).Should().BeFalse();
    }

    [Fact]
    public void Publish_CoversEveryTileOfAFootprint_NotJustTheCorner()
    {
        PlacedTiles tiles = new();

        tiles.Publish(Farm, [new PlacedItem(1, ItemCatalog.BlueCouch, 10, 10, 0), new PlacedItem(2, ItemCatalog.Chest, 3, 3, 0)]);

        tiles.IsCovered(Farm, 10, 10).Should().BeTrue();
        tiles.IsCovered(Farm, 12, 10).Should().BeTrue("the couch is three tiles wide");
        tiles.IsCovered(Farm, 13, 10).Should().BeFalse();
        tiles.IsCovered(Farm, 3, 3).Should().BeTrue();
        tiles.IsCovered(Farm, 3, 4).Should().BeFalse();
        tiles.ItemsOf(Farm).Should().HaveCount(2);
    }

    [Fact]
    public void Publish_ReplacesThePreviousSnapshot_AndOtherInstancesAreUntouched()
    {
        PlacedTiles tiles = new();
        MapInstance other = new(GameMaps.Farm, Guid.Parse("22222222-2222-2222-2222-222222222222"));
        tiles.Publish(Farm, [new PlacedItem(1, ItemCatalog.Chest, 3, 3, 0)]);
        tiles.Publish(other, [new PlacedItem(2, ItemCatalog.Chest, 3, 3, 0)]);

        tiles.Publish(Farm, []);

        tiles.IsCovered(Farm, 3, 3).Should().BeFalse("a picked-up piece frees its tile");
        tiles.IsCovered(other, 3, 3).Should().BeTrue("another player's farm is a different instance");
    }

    [Fact]
    public void IsSprinklerWatered_ReadsThePublishedPieces()
    {
        PlacedTiles tiles = new();
        tiles.Publish(Farm, [new PlacedItem(1, ItemCatalog.Sprinkler, 5, 5, 0), new PlacedItem(2, ItemCatalog.Chest, 9, 9, 0)]);

        tiles.IsSprinklerWatered(Farm, 5, 6).Should().BeTrue();
        tiles.IsSprinklerWatered(Farm, 5, 5).Should().BeFalse();
        tiles.IsSprinklerWatered(Farm, 9, 10).Should().BeFalse();
    }

    [Fact]
    public void Publish_TakesACopy_SoLaterChangesToTheSourceDoNotLeakIn()
    {
        PlacedTiles tiles = new();
        List<PlacedItem> source = [new PlacedItem(1, ItemCatalog.Chest, 3, 3, 0)];
        tiles.Publish(Farm, source);

        source.Clear();

        tiles.ItemsOf(Farm).Should().HaveCount(1);
    }

    [Fact]
    public void GridFor_FarmWithBothLayers_BlocksWhatEitherLayerBlocks_AndIsKeptUntilALayerChanges()
    {
        MapDefinition map = FarmMap(placeable: true, nodes: true);
        WalkGrid nodeGrid = map.Grid.Clone();
        nodeGrid.Block(1, 1, 1, 1);
        WalkGrid placedGrid = map.Grid.Clone();
        placedGrid.Block(2, 2, 1, 1);
        Layer placed = new() { Grid = placedGrid };
        NodeLayer nodes = new() { Grid = nodeGrid };
        InstanceWorld world = new(placed, nodes);

        WalkGrid joined = world.GridFor(Farm, map);

        joined.IsBlocked(1, 1).Should().BeTrue("a tree blocks");
        joined.IsBlocked(2, 2).Should().BeTrue("a fence blocks");
        joined.IsBlocked(3, 3).Should().BeFalse();
        world.GridFor(Farm, map).Should().BeSameAs(joined, "nothing changed, nothing is rebuilt");

        // A tree is chopped: the node layer swaps in a new grid, and the join follows.
        WalkGrid chopped = map.Grid.Clone();
        nodes.Grid = chopped;
        WalkGrid after = world.GridFor(Farm, map);

        after.Should().NotBeSameAs(joined);
        after.IsBlocked(1, 1).Should().BeFalse();
        after.IsBlocked(2, 2).Should().BeTrue();
    }

    [Fact]
    public void GridFor_OneLayerNotLoaded_IsTheOtherLayersGridUnchanged()
    {
        MapDefinition map = FarmMap(placeable: true, nodes: true);
        WalkGrid placedGrid = map.Grid.Clone();
        placedGrid.Block(2, 2, 1, 1);

        new InstanceWorld(new Layer { Grid = placedGrid }, new NodeLayer { Grid = map.Grid }).GridFor(Farm, map).Should().BeSameAs(placedGrid);
        new InstanceWorld(new Layer { Grid = map.Grid }, new NodeLayer { Grid = placedGrid }).GridFor(Farm, map).Should().BeSameAs(placedGrid);
    }

    [Fact]
    public void GridFor_MapsWithOneLayer_UseThatLayerOnly()
    {
        MapDefinition room = FarmMap(placeable: true, nodes: false);
        MapDefinition forest = FarmMap(placeable: false, nodes: true);
        WalkGrid roomPlaced = room.Grid.Clone();
        WalkGrid forestNodes = forest.Grid.Clone();

        new InstanceWorld(new Layer { Grid = roomPlaced }, new NodeLayer { Grid = forestNodes }).GridFor(Farm, room).Should().BeSameAs(roomPlaced);
        new InstanceWorld(new Layer { Grid = roomPlaced }, new NodeLayer { Grid = forestNodes }).GridFor(MapInstance.Shared("forest"), forest).Should().BeSameAs(forestNodes);
    }

    private static MapDefinition FarmMap(bool placeable, bool nodes)
        => new("farm", new WalkGrid(8, 8, 16, new bool[64]), 0, 0, placeable: placeable, nodeSites: nodes ? [new NodeSite(4, 4, NodeKind.Stone, 0)] : null);

    private class Layer : IInstanceWorld
    {
        public WalkGrid Grid { get; set; } = null!;

        public ValueTask PrepareAsync(MapInstance instance, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public WalkGrid GridFor(MapInstance instance, MapDefinition map) => this.Grid;
    }

    private sealed class NodeLayer : Layer, IResourceNodeService
    {
        public Task<NodeSnapshotView> GetAsync(Nalix.Abstractions.Networking.IConnection connection, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Nosum.Abstractions.Farm.FarmUseOutcome?> TryUseAsync(NodeUseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AdvanceDayAsync(long day, bool seasonChanged, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
