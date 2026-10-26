// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
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
    // A stand-in for the cave sheet: tile 0 is the floor, tile 1 the wall. The sheet itself is drawn later (ADR-042 P2).
    private const string Cave = """
        name cave
        image cave.png
        tilewidth 16
        tileheight 16
        columns 4
        tilecount 4
        tile 1 Impassable
        """;

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
