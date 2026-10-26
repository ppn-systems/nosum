// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Nodes;
using Nosum.MapGen;
using Nosum.MapGen.Sheets;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Tools;

/// <summary>
/// The drawn sheets (docs/adr/ADR-042 P2) are byte-stable, the committed pictures are what the tool draws, every tile a trait file names exists in
/// its picture, the cave's tiles 0 and 1 carry the traits the mine generator relies on, and the marks of the <c>Paths</c> layer are drawn.
/// </summary>
public sealed class MapGenSheetsTests
{
    private static readonly string s_maps = RepoFiles.Path("art", "own", "Maps");
    private static readonly string s_tilesets = RepoFiles.Path("art", "own", "source", "tilesets");

    public static TheoryData<string> SheetNames => new(SheetCatalog.All.Select(s => s.Name));

    public static TheoryData<string> TileSetNames => new(SheetCatalog.All.Select(s => s.TileSet));

    [Theory]
    [MemberData(nameof(SheetNames))]
    public void Render_SameSheetTwice_GivesTheSameBytes(string name)
    {
        SheetInfo sheet = SheetCatalog.All.Single(s => s.Name == name);

        byte[] first = sheet.Render().ToPng();
        byte[] second = sheet.Render().ToPng();

        first.Should().Equal(second);
    }

    [Fact]
    public void Differences_CommittedPictures_AreTheDrawing()
    {
        IReadOnlyList<string> stale = SheetCatalog.Differences(s_maps);

        stale.Should().BeEmpty("each art/own/Maps sheet must be what `dotnet run --project tools/Nosum.MapGen -- sheets` writes");
    }

    [Theory]
    [MemberData(nameof(TileSetNames))]
    public void TileSetFile_EveryNamedIndex_ExistsInItsPicture(string tileSet)
    {
        SheetInfo sheet = SheetCatalog.ByTileSet(tileSet)!;
        TilesetData data = TilesetData.Load(s_tilesets, tileSet);
        (int width, int height) = PngSize(Path.Combine(s_maps, sheet.File));
        int tilesInPicture = (width / SheetInfo.TileSize) * (height / SheetInfo.TileSize);

        data.Image.Should().Be(sheet.File);
        data.Columns.Should().Be(width / SheetInfo.TileSize);
        data.TileCount.Should().Be(sheet.TileCount);
        data.TileCount.Should().BeLessThanOrEqualTo(tilesInPicture);
        foreach (int index in data.Traits.Keys)
        {
            index.Should().BeLessThan(tilesInPicture, $"trait index {index} of {tileSet} must exist in {sheet.File}");
        }
    }

    [Fact]
    public void Cave_Tile0_IsAWalkableFloor()
    {
        TilesetData cave = TilesetData.Load(s_tilesets, MineGenerator.Tileset);

        cave.Traits.TryGetValue(MineGenerator.FloorTile, out TileTraits floor);

        (floor & (TileTraits.Water | TileTraits.Impassable)).Should().Be(TileTraits.None);
    }

    [Fact]
    public void Cave_Tile1_IsSolidAndNotPassable()
    {
        TilesetData cave = TilesetData.Load(s_tilesets, MineGenerator.Tileset);

        cave.Traits.TryGetValue(MineGenerator.WallTile, out TileTraits wall);

        wall.Should().HaveFlag(TileTraits.Impassable);
        wall.Should().NotHaveFlag(TileTraits.Passable);
    }

    [Fact]
    public void Terrain_FillTraits_AreTheRulesTraits()
    {
        TilesetData terrain = TilesetData.Load(s_tilesets, "terrain");

        terrain.Traits[TerrainSheet.Fill(TerrainSheet.Ground.Dirt, 0)].Should().HaveFlag(TileTraits.Diggable);
        terrain.Traits[TerrainSheet.Fill(TerrainSheet.Ground.Shallow, 0)].Should().HaveFlag(TileTraits.Water);
        terrain.Traits[TerrainSheet.Fill(TerrainSheet.Ground.Deep, 0)].Should().HaveFlag(TileTraits.Water);
        terrain.Traits[TerrainSheet.Fill(TerrainSheet.Ground.Deep, 0)].Should().HaveFlag(TileTraits.Impassable);
        terrain.Traits[TerrainSheet.Fill(TerrainSheet.Ground.Cliff, 0)].Should().HaveFlag(TileTraits.Impassable);
        terrain.Traits[TerrainSheet.Fill(TerrainSheet.Ground.Path, 0)].Should().HaveFlag(TileTraits.Passable);
        terrain.Traits.Should().NotContainKey(TerrainSheet.Fill(TerrainSheet.Ground.Grass, 0));
    }

    [Fact]
    public void Markers_EveryNodeSiteIndex_IsDrawn()
    {
        Canvas markers = SheetCatalog.ByTileSet("Paths")!.Render();
        int[] indices = Enumerable.Range(0, MarkerSheet.TileCount)
            .Where(index => NodeSites.TryMap(index, out _, out _) || NodeSites.IsForageIndex(index))
            .ToArray();

        indices.Should().NotBeEmpty();
        indices.Where(index => !TileHasInk(markers, index)).Should().BeEmpty("each marker the node sites read needs a picture");
    }

    [Fact]
    public void Sources_ListEverySheet()
    {
        string sources = File.ReadAllText(RepoFiles.Path("art", "own", "SOURCES.md"));

        foreach (SheetInfo sheet in SheetCatalog.All)
        {
            sources.Should().Contain("`Maps/" + sheet.File + "`", $"{sheet.File} needs an entry in art/own/SOURCES.md");
        }

        sources.Should().Contain("drawn in code by tools/Nosum.MapGen sheets");
    }

    // The width and height of a PNG, read from its IHDR chunk.
    private static (int Width, int Height) PngSize(string path)
    {
        byte[] head = new byte[24];
        using FileStream stream = File.OpenRead(path);
        _ = stream.Read(head, 0, head.Length);
        return (ReadBigEndian(head, 16), ReadBigEndian(head, 20));
    }

    private static int ReadBigEndian(byte[] bytes, int at)
        => (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];

    private static bool TileHasInk(Canvas canvas, int index)
    {
        int left = index % SheetInfo.Columns * SheetInfo.TileSize;
        int top = index / SheetInfo.Columns * SheetInfo.TileSize;
        for (int y = top; y < top + SheetInfo.TileSize; y++)
        {
            for (int x = left; x < left + SheetInfo.TileSize; x++)
            {
                if (canvas.Pixels[(((y * canvas.Width) + x) * 4) + 3] == 255)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
