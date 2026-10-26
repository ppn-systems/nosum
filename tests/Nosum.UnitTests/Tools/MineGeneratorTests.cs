// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Mines;
using Nosum.MapGen;
using Xunit;

namespace Nosum.UnitTests.Tools;

/// <summary>
/// The mine generator (docs/adr/ADR-042 D4): every floor is generated in memory from its fixed seed, passes the D2 mine contract the way the game reads
/// it, and gives the same bytes every time. Nothing is committed here; <c>mine all --check</c> compares the committed floors.
/// </summary>
public sealed class MineGeneratorTests
{
    // The cave sheet as cave.tileset describes it: tile 0 is the floor, tile 1 the wall, 16 to 31 the wall edges.
    private const string Cave = """
        name cave
        image cave.png
        tilewidth 16
        tileheight 16
        columns 16
        tilecount 48
        tile 1 Impassable
        tile 16-31 Impassable
        """;

    private static readonly int[] s_floorVariants = [0, 2, 3, 4];
    private static readonly int[] s_rockDecor = [35, 39, 45, 46];

    public static IEnumerable<object[]> Floors() => Enumerable.Range(1, MineRules.MaxFloor).Select(floor => new object[] { floor });

    [Fact]
    public void Generate_SameFloor_IsByteIdentical()
    {
        TilesetData cave = TilesetData.Parse(Cave, "cave.tileset");

        string first = MineGenerator.Generate(7, cave);
        string second = MineGenerator.Generate(7, cave);

        second.Should().Be(first);
    }

    [Theory]
    [MemberData(nameof(Floors))]
    public void Floor_PassesTheMineContract(int floor)
    {
        TilesetData cave = TilesetData.Parse(Cave, "cave.tileset");

        string tmx = MineGenerator.Generate(floor, cave);

        MineGenerator.Verify(floor, tmx).Should().BeEmpty();
    }

    [Fact]
    public void Floor_LooksVaried_WithFloorVariantsRockDecorAndWallEdges()
    {
        TilesetData cave = TilesetData.Parse(Cave, "cave.tileset");

        TileMap map = TmxReader.Parse(MineGenerator.Generate(12, cave));
        int[] back = map.FindLayer("Back")!.Gids;
        int[] walls = map.FindLayer("Buildings")!.Gids;

        // Gids are tile index + 1: the floor variants are tiles 0, 2, 3 and 4; the decor 35, 39, 45 and 46.
        back.Where(g => g != 0).Select(g => g - 1).Distinct().Should().Contain(s_floorVariants);
        back.Select(g => g - 1).Should().Contain(s_rockDecor);
        walls.Where(g => g != 0).Select(g => g - 1).Should().Contain(g => g >= 16 && g <= 31, "a wall next to open ground takes its edge");
    }

    [Fact]
    public void Floor_NamesTheSheetOneFolderUp_BecauseFloorsLiveInMines()
    {
        TilesetData cave = TilesetData.Parse(Cave, "cave.tileset");

        TileMap map = TmxReader.Parse(MineGenerator.Generate(3, cave));

        map.TileSets.Should().ContainSingle().Which.ImageSource.Should().Be("../cave.png");
    }

    [Fact]
    public void Verify_ArrivalInAWall_IsReported()
    {
        int[] back = new int[MineGenerator.Size * MineGenerator.Size];
        int[] walls = Enumerable.Repeat(2, back.Length).ToArray();
        string walled = TmxWriter.Write(MineGenerator.Size, MineGenerator.Size, [new PlacedTileset(TilesetData.Parse(Cave, "cave.tileset"), 1)], [new MapLayer("Back", back), new MapLayer("Buildings", walls)], []);

        IReadOnlyList<string> problems = MineGenerator.Verify(1, walled);

        problems.Should().ContainSingle().Which.Should().Contain("arrival tile");
    }

    [Fact]
    public void Verify_CaveTooSmall_IsReported()
    {
        MineLayouts.Layout arrival = MineLayouts.For(1);
        int[] back = new int[MineGenerator.Size * MineGenerator.Size];
        int[] walls = new int[back.Length];
        for (int i = 0; i < walls.Length; i++)
        {
            int x = i % MineGenerator.Size, y = i / MineGenerator.Size;
            walls[i] = Math.Max(Math.Abs(x - arrival.SpawnX), Math.Abs(y - arrival.SpawnY)) > 1 ? 2 : 0;
        }

        string tiny = TmxWriter.Write(MineGenerator.Size, MineGenerator.Size, [new PlacedTileset(TilesetData.Parse(Cave, "cave.tileset"), 1)], [new MapLayer("Back", back), new MapLayer("Buildings", walls)], []);

        IReadOnlyList<string> problems = MineGenerator.Verify(1, tiny);

        problems.Should().Contain(p => p.Contains("needs", StringComparison.Ordinal) && p.Contains("tiles", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_CaveSheetWithoutAWallTile_IsRejected()
    {
        TilesetData oneTile = TilesetData.Parse("name cave\nimage cave.png\ntilewidth 16\ntileheight 16\ncolumns 1\ntilecount 1\n", "cave.tileset");

        Action act = () => MineGenerator.Generate(1, oneTile);

        act.Should().Throw<InvalidDataException>().WithMessage("*needs a wall tile 1*");
    }
}
