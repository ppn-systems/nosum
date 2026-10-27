// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.MapGen;
using Nosum.MapGen.Sheets;
using Xunit;

namespace Nosum.UnitTests.Tools;

/// <summary>The terrain pass of the layout compiler: grass variants and the traitless edge layer that borders a pond.</summary>
public sealed class AutoTerrainTests
{
    private const string Terrain = """
        name terrain
        image terrain.png
        tilewidth 16
        tileheight 16
        columns 16
        tilecount 126
        tile 20-23 Water
        """;

    // A 5 x 5 meadow with a 3 x 3 pond of shallow water in the middle.
    private const string Pond = """
        legend
        a terrain 0
        w terrain 20
        layer Back
        aaaaa
        awwwa
        awwwa
        awwwa
        aaaaa
        """;

    private static TilesetData Resolve(string name) => TilesetData.Parse(Terrain, "terrain.tileset");

    [Fact]
    public void Compile_WithoutTerrainPass_HasNoEdgeLayer()
    {
        TileMap map = TmxReader.Parse(LayoutCompiler.Compile(Pond, "pond.txt", Resolve));

        map.FindLayer(AutoTerrain.EdgeLayer).Should().BeNull();
        map.FindLayer("Back")!.Gids.Where(g => g == 1).Should().HaveCount(16, "every grass cell keeps the legend's tile");
    }

    [Fact]
    public void Compile_PondWithTerrainPass_EdgesTheShoreAndKeepsTheFills()
    {
        TileMap map = TmxReader.Parse(LayoutCompiler.Compile(Pond, "pond.txt", Resolve, terrainPass: true));

        TileLayer back = map.FindLayer("Back")!;
        TileLayer edge = map.FindLayer(AutoTerrain.EdgeLayer)!;
        back.Gids.Where((_, i) => i / 5 is >= 1 and <= 3 && i % 5 is >= 1 and <= 3).Should().OnlyContain(g => g == 21, "the pond's fills stay water");
        back.Gids.Where((_, i) => i / 5 is 0 or 4 || i % 5 is 0 or 4).Should().OnlyContain(g => g >= 1 && g <= 4, "grass takes one of its four fills");
        edge.Gids[(2 * 5) + 2].Should().Be(0, "the middle of the pond has no edge");
        edge.Gids[(1 * 5) + 1].Should().BeGreaterThan(TerrainSheet.FirstTransition, "the pond's corner cell is a shore tile");
        edge.Gids.Where((g, i) => g > 0 && (i / 5 is 0 or 4 || i % 5 is 0 or 4)).Should().BeEmpty("the grass cells keep plain grass");
        map.GetTraits(edge.Gids[(1 * 5) + 1]).Should().Be(TileTraits.None, "edge tiles carry no traits");
    }

    [Fact]
    public void Compile_TerrainPassTwice_GivesTheSameBytes()
    {
        string first = LayoutCompiler.Compile(Pond, "pond.txt", Resolve, terrainPass: true);
        string second = LayoutCompiler.Compile(Pond, "pond.txt", Resolve, terrainPass: true);

        first.Should().Be(second);
    }

    [Fact]
    public void Compile_LoneWaterCellInGrass_IsCoveredWithGrass()
    {
        const string Lone = """
            legend
            a terrain 0
            w terrain 20
            layer Back
            aaaaa
            aaaaa
            aawaa
            aaaaa
            aaaaa
            """;

        TileMap map = TmxReader.Parse(LayoutCompiler.Compile(Lone, "lone.txt", Resolve, terrainPass: true));

        TileLayer edge = map.FindLayer(AutoTerrain.EdgeLayer)!;
        edge.Gids[(2 * 5) + 2].Should().BeInRange(1, 4, "a lone water cell is covered with grass instead of a floating shore corner");
        edge.Gids.Where((g, i) => g > 0 && i != (2 * 5) + 2).Should().BeEmpty("the grass around it draws nothing");
        map.FindLayer("Back")!.Gids[(2 * 5) + 2].Should().Be(21, "the lone cell keeps its water fill and traits");
    }

    [Fact]
    public void Compile_DiagonalWaterNeighbour_LeavesGrassPlain()
    {
        const string Diagonal = """
            legend
            a terrain 0
            w terrain 20
            layer Back
            aaaaa
            aawwa
            aawwa
            aaaaa
            aaaaa
            """;

        TileMap map = TmxReader.Parse(LayoutCompiler.Compile(Diagonal, "diagonal.txt", Resolve, terrainPass: true));

        TileLayer edge = map.FindLayer(AutoTerrain.EdgeLayer)!;
        edge.Gids.Where((g, i) => g > 0 && map.FindLayer("Back")!.Gids[i] <= 4).Should().BeEmpty("a grass cell never takes a water corner, even with water on its diagonal");
    }
}
