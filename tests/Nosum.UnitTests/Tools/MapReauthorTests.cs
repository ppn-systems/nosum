// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.MapReauthor;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Tools;

/// <summary>
/// The re-author tool (docs/adr/ADR-040) swaps the terrain art of a map and nothing else: collision, exits, spawn, tilling, water and every
/// object group of the converted Farm must be the placeholder's, and the committed files must be what the tool writes.
/// </summary>
public sealed partial class MapReauthorTests
{
    private static readonly string s_original = File.ReadAllText(RepoFiles.Path("assets", "Maps", "Farm.tmx"));
    private static readonly string s_own = File.ReadAllText(RepoFiles.Path("art", "own", "Maps", "Farm.tmx"));

    private static MapSpec FarmSpec()
    {
        _ = GameMaps.TryGet(GameMaps.Farm, out MapSpec? spec);
        return spec!;
    }

    [Fact]
    public void KindTable_Farm_ListsEveryTileTheGroundLayerPlaces()
    {
        KindTable table = KindTable.Embedded();
        TileMap map = TmxReader.Parse(s_original);
        TileLayer ground = map.FindLayer("Back")!;

        List<string> missing = [];
        foreach (int gid in new HashSet<int>(ground.Gids))
        {
            int set = map.FindTileSet(gid);
            if (gid > 0 && set >= 0 && !table.TryGet(Reauthor.SheetName(map.TileSets[set].ImageSource), gid - map.TileSets[set].FirstGid, out _))
            {
                missing.Add($"{map.TileSets[set].ImageSource}:{gid - map.TileSets[set].FirstGid}");
            }
        }

        missing.Should().BeEmpty("every placed ground tile needs a kind (or `other`) in tools/Nosum.MapReauthor/terrain-kinds.csv");
    }

    [Fact]
    public void KindTable_DuplicateTile_IsRejected()
    {
        System.Action parse = () => KindTable.Parse("sheet,index,kind\na,1,grass\na,1,dirt\n");

        parse.Should().Throw<System.FormatException>();
    }

    [Fact]
    public void Run_Farm_CommittedFilesAreWhatTheToolWrites()
    {
        using StringWriter log = new();

        Program.Run(RepoFiles.Path(), "Farm", check: true, log).Should().Be(0, log.ToString() + " Regenerate: dotnet run --project tools/Nosum.MapReauthor -- reauthor Farm");
    }

    [Fact]
    public void Run_Farm_EveryLayerExceptTheGroundIsCopiedCharacterForCharacter()
    {
        foreach (string name in new[] { "Buildings", "Paths", "Front", "AlwaysFront", "AlwaysFront2" })
        {
            Layer(s_own, name).Should().Be(Layer(s_original, name), $"{name} is a gameplay or object layer");
        }

        // Everything from the first object group on (warps of the Paths objects, TileData, node sites) is untouched.
        s_own[s_own.IndexOf("<objectgroup", System.StringComparison.Ordinal)..].Should().Be(s_original[s_original.IndexOf("<objectgroup", System.StringComparison.Ordinal)..]);

        // The map properties (Warp, Outdoors) and the placeholder tilesets keep their text, and the own tileset is appended after them.
        string head = s_original[..s_original.IndexOf("\n  <layer ", System.StringComparison.Ordinal)];
        head = NextLayerId().Replace(head, string.Empty);
        NextLayerId().Replace(s_own, string.Empty).Should().Contain(head);
    }

    [Fact]
    public void Run_Farm_GroundCellsThatStayKeepTheirPlaceholderValue()
    {
        TileMap original = TmxReader.Parse(s_original);
        TileMap own = TmxReader.Parse(s_own);
        TileLayer before = original.FindLayer("Back")!;
        TileLayer after = own.FindLayer("Back")!;

        int ownFirstGid = own.TileSets[^1].FirstGid;
        int same = 0;
        for (int i = 0; i < before.Gids.Length; i++)
        {
            if (after.Gids[i] < ownFirstGid)
            {
                after.Gids[i].Should().Be(before.Gids[i]);
                same++;
            }
            else
            {
                before.Gids[i].Should().BePositive("an empty cell gets no tile");
            }
        }

        same.Should().BeLessThan(50, "almost every ground cell is re-authored");
    }

    [Fact]
    public void BuildWalkGrid_ConvertedFarm_BlocksExactlyTheSameTiles()
    {
        MapSpec spec = FarmSpec();
        TileMap original = TmxReader.Parse(s_original);
        TileMap own = TmxReader.Parse(s_own);

        WalkGrid before = spec.BuildWalkGrid(original);
        WalkGrid after = spec.BuildWalkGrid(own);

        List<(int, int)> different = [];
        for (int y = 0; y < original.Height; y++)
        {
            for (int x = 0; x < original.Width; x++)
            {
                if (before.IsBlocked(x, y) != after.IsBlocked(x, y))
                {
                    different.Add((x, y));
                }
            }
        }

        different.Should().BeEmpty();
        (after.Width, after.Height).Should().Be((before.Width, before.Height));
    }

    [Fact]
    public void FromMap_ConvertedFarm_TillsAndRefillsOnTheSameTiles()
    {
        MapSpec spec = FarmSpec();
        TileMap original = TmxReader.Parse(s_original);
        TileMap own = TmxReader.Parse(s_own);
        TillGrid tillBefore = TillGrid.FromMap(original, spec.BuildWalkGrid(original));
        TillGrid tillAfter = TillGrid.FromMap(own, spec.BuildWalkGrid(own));
        WaterGrid waterBefore = WaterGrid.FromMap(original);
        WaterGrid waterAfter = WaterGrid.FromMap(own);

        int tillable = 0, water = 0;
        for (int y = 0; y < original.Height; y++)
        {
            for (int x = 0; x < original.Width; x++)
            {
                tillAfter.IsTillable(x, y).Should().Be(tillBefore.IsTillable(x, y), $"tillable at {x},{y}");
                waterAfter.IsWater(x, y).Should().Be(waterBefore.IsWater(x, y), $"water at {x},{y}");
                tillable += tillBefore.IsTillable(x, y) ? 1 : 0;
                water += waterBefore.IsWater(x, y) ? 1 : 0;
            }
        }

        tillable.Should().BeGreaterThan(1000, "the farm has a large tillable area");
        water.Should().BeGreaterThan(100, "the farm has ponds");
    }

    [Fact]
    public void Resolve_ConvertedFarm_HasTheSameWarpsSpawnAndNodeSites()
    {
        MapSpec spec = FarmSpec();
        TileMap original = TmxReader.Parse(s_original);
        TileMap own = TmxReader.Parse(s_own);
        WalkGrid gridBefore = spec.BuildWalkGrid(original);
        WalkGrid gridAfter = spec.BuildWalkGrid(own);

        spec.ResolveWarps(own).Should().Equal(spec.ResolveWarps(original));
        SpawnPoint.Find(gridAfter, spec.SpawnTileX, spec.SpawnTileY).Should().Be(SpawnPoint.Find(gridBefore, spec.SpawnTileX, spec.SpawnTileY));
        spec.ResolveNodeSites(own, gridAfter).Should().Equal(spec.ResolveNodeSites(original, gridBefore));
        spec.ResolveForageSites(own, gridAfter).Should().Equal(spec.ResolveForageSites(original, gridBefore));
        (own.Width, own.Height, own.TileWidth).Should().Be((original.Width, original.Height, original.TileWidth));
        own.Properties.Should().BeEquivalentTo(original.Properties);
    }

    [Fact]
    public void Run_Farm_OwnTilesStayInsideTheOwnTileset()
    {
        TileMap own = TmxReader.Parse(s_own);
        TileSet terrain = own.TileSets[^1];
        Rgba image = Rgba.Read(RepoFiles.Path("art", "own", "Maps", "own_terrain.png"));

        terrain.ImageSource.Should().Be(OwnTileset.Name);
        (image.Width, image.Height).Should().Be((OwnTileset.Columns * OwnTileset.TileSize, OwnTileset.Rows * OwnTileset.TileSize));
        terrain.TileCount.Should().Be(OwnTileset.TileCount);
        foreach (string name in new[] { "Back", "BackEdge" })
        {
            foreach (int gid in own.FindLayer(name)!.Gids)
            {
                if (gid >= terrain.FirstGid)
                {
                    (gid - terrain.FirstGid).Should().BeLessThan(OwnTileset.TileCount);
                }
            }
        }

        own.Layers.Select(l => l.Name).Should().ContainInOrder("Back", "BackEdge", "Buildings");
        own.FindLayer("BackEdge")!.Gids.Should().Contain(g => g >= terrain.FirstGid, "the farm's ponds and grass patches have edges");
    }

    [Fact]
    public void Build_PixelLabSets_IsTheCommittedTileset()
    {
        Rgba built = TilesetBuilder.Build(RepoFiles.Path("art", "own", "source", "pixellab"));
        Rgba committed = Rgba.Read(RepoFiles.Path("art", "own", "Maps", "own_terrain.png"));

        (built.Width, built.Height).Should().Be((committed.Width, committed.Height));
        built.Pixels.Should().Equal(committed.Pixels, "art/own/Maps/own_terrain.png must be what `Nosum.MapReauthor tileset` writes");
    }

    [Fact]
    public void Resolve_Uniform_NeedsNoCornerTile()
    {
        TerrainKind[] kinds = Cells(3, 3, TerrainKind.Grass);

        TerrainEdges.Resolve(kinds, 3, 3).Should().AllBeEquivalentTo(-1);
    }

    [Fact]
    public void Resolve_WaterInsideGrass_UsesTheWaterGrassSetAroundThePond()
    {
        TerrainKind[] kinds = Cells(7, 7, TerrainKind.Grass);
        Fill(kinds, 7, 2, 2, 3, 3, TerrainKind.Water);

        int[] tiles = TerrainEdges.Resolve(kinds, 7, 7);

        int set = WangSet.Find(TerrainKind.Water, TerrainKind.Grass);
        tiles[(3 * 7) + 3].Should().Be(-1, "the middle of the pond has water in all four corners");
        tiles[(2 * 7) + 2].Should().BeInRange(OwnTileset.Corner(set, 0), OwnTileset.Corner(set, 15));
        tiles[0].Should().Be(-1, "grass far from the pond");
    }

    [Fact]
    public void Resolve_WaterInsideDirt_MeetsThroughGrass()
    {
        TerrainKind[] kinds = Cells(7, 7, TerrainKind.Dirt);
        Fill(kinds, 7, 2, 2, 3, 3, TerrainKind.Water);

        int[] tiles = TerrainEdges.Resolve(kinds, 7, 7);

        int waterGrass = WangSet.Find(TerrainKind.Water, TerrainKind.Grass);
        int grassDirt = WangSet.Find(TerrainKind.Grass, TerrainKind.Dirt);
        tiles[(2 * 7) + 2].Should().BeInRange(OwnTileset.Corner(waterGrass, 0), OwnTileset.Corner(waterGrass, 15), "the water cell's shore is grass");
        tiles[(1 * 7) + 2].Should().BeInRange(OwnTileset.Corner(grassDirt, 0), OwnTileset.Corner(grassDirt, 15), "the dirt cell around the shore turns from grass to dirt");
        tiles.Should().OnlyContain(t => t < 0 || (t >= OwnTileset.FillCount && t < OwnTileset.TileCount));
    }

    [Fact]
    public void Resolve_SandBeachBesideWater_UsesTheWaterSandSet()
    {
        TerrainKind[] kinds = Cells(4, 4, TerrainKind.Sand);
        Fill(kinds, 4, 0, 0, 4, 2, TerrainKind.Water);

        int[] tiles = TerrainEdges.Resolve(kinds, 4, 4);

        int set = WangSet.Find(TerrainKind.Water, TerrainKind.Sand);
        tiles[(1 * 4) + 1].Should().BeInRange(OwnTileset.Corner(set, 0), OwnTileset.Corner(set, 15));
    }

    [Fact]
    public void Fill_UnsupportedTraits_AreRefused()
    {
        OwnTileset.Fill(TerrainKind.Grass, TileTraits.Water, 0).Should().Be(-1);
        OwnTileset.Fill(TerrainKind.Water, TileTraits.Water | TileTraits.Impassable, 3).Should().BePositive();
        OwnTileset.Fill(TerrainKind.Path, TileTraits.None, 0).Should().Be(-1, "there is no own path set yet");
    }

    private static TerrainKind[] Cells(int width, int height, TerrainKind kind)
    {
        TerrainKind[] cells = new TerrainKind[width * height];
        System.Array.Fill(cells, kind);
        return cells;
    }

    private static void Fill(TerrainKind[] cells, int width, int left, int top, int w, int h, TerrainKind kind)
    {
        for (int y = top; y < top + h; y++)
        {
            for (int x = left; x < left + w; x++)
            {
                cells[(y * width) + x] = kind;
            }
        }
    }

    private static string Layer(string xml, string name)
    {
        int start = xml.IndexOf("name=\"" + name + "\" width", System.StringComparison.Ordinal);
        start = xml.LastIndexOf("  <layer ", start, System.StringComparison.Ordinal);
        int end = xml.IndexOf("</layer>\n", start, System.StringComparison.Ordinal) + "</layer>\n".Length;
        return xml[start..end];
    }

    [GeneratedRegex("nextlayerid=\"\\d+\"")]
    private static partial Regex NextLayerId();
}
