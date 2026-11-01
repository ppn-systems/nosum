// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class NodeFieldTests
{
    private static ResourceNode Node(int id, int x, int y, NodeKind kind = NodeKind.Stone)
        => new(id, x, y, kind, 0, 0, NodeCatalog.HitsFor(kind, 0));

    private static WalkGrid OpenGrid(int width = 64, int height = 64) => new(width, height, 16, new bool[width * height]);

    [Fact]
    public void Add_PutsTheNodeInItsChunkAndOnItsTile()
    {
        NodeField field = new();

        field.Add(Node(1, 3, 4));
        field.Add(Node(2, 17, 4));
        field.Add(Node(3, 18, 33));

        field.Count.Should().Be(3);
        field.TryGetAt(3, 4, out ResourceNode at).Should().BeTrue();
        at.Id.Should().Be(1);
        field.HasNodeAt(17, 4).Should().BeTrue();
        field.HasNodeAt(4, 4).Should().BeFalse();
        List<ResourceNode> chunk = [];
        field.CopyChunk(0, 0, chunk);
        chunk.Select(n => n.Id).Should().Equal(1);
        chunk.Clear();
        field.CopyChunk(1, 2, chunk);
        chunk.Select(n => n.Id).Should().Equal(3);
        NodeField.ChunkOf(15).Should().Be(0);
        NodeField.ChunkOf(16).Should().Be(1);
    }

    [Fact]
    public void Add_ATakenIdOrTile_IsRefused()
    {
        NodeField field = new();
        field.Add(Node(1, 3, 4));

        Action sameId = () => field.Add(Node(1, 9, 9));
        Action sameTile = () => field.Add(Node(2, 3, 4));

        sameId.Should().Throw<InvalidOperationException>();
        sameTile.Should().Throw<InvalidOperationException>();
        field.Count.Should().Be(1);
    }

    [Fact]
    public void Remove_FreesTheTileAndEmptiesTheChunk()
    {
        NodeField field = new();
        field.Add(Node(1, 3, 4));

        field.Remove(1).Should().BeTrue();
        field.Remove(1).Should().BeFalse();

        field.HasNodeAt(3, 4).Should().BeFalse();
        List<ResourceNode> chunk = [];
        field.CopyChunk(0, 0, chunk);
        chunk.Should().BeEmpty();
        field.Add(Node(2, 3, 4));
    }

    [Fact]
    public void Replace_ChangesTheStateOfANodeThatStaysOnItsTile()
    {
        NodeField field = new();
        field.Add(Node(1, 3, 4, NodeKind.Tree));

        field.Replace(Node(1, 3, 4, NodeKind.Stump)).Should().BeTrue();
        field.Replace(Node(1, 5, 4, NodeKind.Stump)).Should().BeFalse("a node does not move");
        field.Replace(Node(9, 3, 4)).Should().BeFalse("unknown id");

        field.TryGet(1, out ResourceNode node).Should().BeTrue();
        node.Kind.Should().Be(NodeKind.Stump);
    }

    [Fact]
    public void BuildGrid_BlocksTheTilesOfSolidNodesOnly_AndLeavesTheMapGridAlone()
    {
        WalkGrid map = OpenGrid();
        NodeField field = new();
        field.Add(new ResourceNode(1, 3, 4, NodeKind.Tree, TreeVariant.Oak, TreeStage.Grown, NodeCatalog.HitsFor(NodeKind.Tree, TreeStage.Grown)));
        field.Add(Node(2, 5, 4, NodeKind.Stone));
        field.Add(Node(3, 7, 4, NodeKind.Weed));
        field.Add(Node(4, 9, 4, NodeKind.Grass));

        WalkGrid grid = field.BuildGrid(map);

        grid.IsBlocked(3, 4).Should().BeTrue("a grown tree blocks");
        grid.IsBlocked(5, 4).Should().BeTrue();
        grid.IsBlocked(7, 4).Should().BeFalse("a weed never blocks");
        grid.IsBlocked(9, 4).Should().BeFalse("grass never blocks");
        map.IsBlocked(3, 4).Should().BeFalse("the shared map grid is never changed");
    }

    [Fact]
    public void BuildGrid_WithNothingSolid_ReturnsTheMapGridItself()
    {
        WalkGrid map = OpenGrid();
        NodeField field = new();
        field.Add(Node(1, 7, 4, NodeKind.Weed));

        field.BuildGrid(map).Should().BeSameAs(map);
    }

    [Fact]
    public void BuildGridInto_BlocksWhatBuildGridBlocks_AndWritesTheScratch()
    {
        WalkGrid map = OpenGrid();
        NodeField field = new();
        field.Add(new ResourceNode(1, 3, 4, NodeKind.Tree, TreeVariant.Oak, TreeStage.Grown, NodeCatalog.HitsFor(NodeKind.Tree, TreeStage.Grown)));
        field.Add(Node(2, 5, 4, NodeKind.Stone));
        WalkGrid scratch = OpenGrid();
        scratch.Block(0, 0, 1, 1); // a stale tile from an earlier build: a rebuild must clear it

        WalkGrid grid = field.BuildGridInto(map, scratch);

        grid.Should().BeSameAs(scratch);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                grid.IsBlocked(x, y).Should().Be(field.BuildGrid(map).IsBlocked(x, y), $"tile {x},{y} matches the copying build");
            }
        }
        map.IsBlocked(3, 4).Should().BeFalse("the shared map grid is never changed");
    }

    [Fact]
    public void BuildGridInto_WithNothingSolid_ReturnsTheMapGridAndLeavesTheScratchAlone()
    {
        WalkGrid map = OpenGrid();
        NodeField field = new();
        field.Add(Node(1, 7, 4, NodeKind.Weed));
        WalkGrid scratch = OpenGrid();

        field.BuildGridInto(map, scratch).Should().BeSameAs(map);
    }

    [Fact]
    public void BuildGridInto_Rebuild_AllocatesNothing()
    {
        WalkGrid map = new(128, 128, 16, new bool[128 * 128]);
        NodeField field = new();
        for (int i = 0; i < 40; i++)
        {
            field.Add(Node(i + 1, 8 + (i % 8) * 3, 8 + (i / 8) * 3, NodeKind.Stone));
        }

        WalkGrid scratch = map.Clone();
        field.BuildGridInto(map, scratch);

        // A chunk change rebuilds the node grid; the walk must not allocate for it (docs/perf/wasm-perf.md, round 5). The smallest of several
        // windows is taken, so a one-off runtime allocation cannot fail the test.
        long smallest = long.MaxValue;
        for (int window = 0; window < 5 && smallest != 0; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
            {
                field.BuildGridInto(map, scratch);
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        smallest.Should().Be(0);
    }

    // Regression for #154: a planted tree stands in the walk grid as nothing until it is grown, and then it blocks.
    [Fact]
    public void BuildGrid_APlantedTree_IsWalkableUntilItIsGrown()
    {
        WalkGrid map = OpenGrid();
        NodeField field = new();
        ResourceNode seed = NodeRules.PlantedTree(1, 4, 4, TreeVariant.Oak);
        field.Add(seed);

        field.BuildGrid(map).Should().BeSameAs(map, "a seed blocks nothing");
        MoveValidator.FootBlocked(field.BuildGrid(map), (4 * 16) + 8, (4 * 16) + 8).Should().BeFalse();

        _ = field.Replace(NodeRules.Age(seed, NodeLimits.DaysPerTreeStage * 3));

        field.BuildGrid(map).IsBlocked(4, 4).Should().BeTrue("a grown tree blocks");
        map.IsBlocked(4, 4).Should().BeFalse("the shared map grid is never changed");
    }

    [Fact]
    public void BuildGrid_ANodeBlocksMovementValidation_TheSameWayABuildingDoes()
    {
        WalkGrid map = OpenGrid();
        NodeField field = new();
        field.Add(Node(1, 4, 4, NodeKind.Stone));
        WalkGrid grid = field.BuildGrid(map);

        MoveValidator.FootBlocked(grid, (4 * 16) + 8, (4 * 16) + 8).Should().BeTrue();
        MoveValidator.FootBlocked(map, (4 * 16) + 8, (4 * 16) + 8).Should().BeFalse();
    }
}
