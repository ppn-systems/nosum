// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Rendering;

namespace Nosum.AssetInventory;

/// <summary>The tiles a map file places, per layer and per sheet.</summary>
/// <param name="Name">The map file, or the group of files the entry sums up.</param>
/// <param name="Package">The package the map belongs to.</param>
/// <param name="Files">How many map files the entry covers.</param>
/// <param name="Layers">The drawn layers in file order.</param>
internal sealed record MapReport(string Name, Package Package, int Files, IReadOnlyList<LayerReport> Layers);

/// <summary>A hosted map file.</summary>
/// <param name="Path">Path under <c>assets/</c>.</param>
/// <param name="Package">The package the map belongs to.</param>
/// <param name="TileWidth">Tile width in pixels.</param>
/// <param name="TileHeight">Tile height in pixels.</param>
/// <param name="Width">Width in tiles.</param>
/// <param name="Height">Height in tiles.</param>
/// <param name="Placed">Tile positions where a drawn layer places a tile.</param>
/// <param name="MapIds">The ids of <see cref="GameMaps"/> that use the file.</param>
internal sealed record MapFile(string Path, Package Package, int TileWidth, int TileHeight, int Width, int Height, int Placed, IReadOnlyList<string> MapIds);

/// <summary>The maps of the game: one file entry each, and the tile detail (the mine floors summed up as one).</summary>
/// <param name="Files">Every hosted map file.</param>
/// <param name="Reports">Tile detail per map.</param>
internal sealed record MapCatalog(IReadOnlyList<MapFile> Files, IReadOnlyList<MapReport> Reports);

/// <summary>The tiles one layer places.</summary>
/// <param name="Name">The layer name.</param>
/// <param name="Sheets">Placed local tile ids per sheet path.</param>
internal sealed record LayerReport(string Name, SortedDictionary<string, SortedSet<int>> Sheets);

/// <summary>
/// Reads every <c>.tmx</c> of <see cref="GameMaps"/> the way <c>MapLoader</c> does: every tileset image is loaded, the layers
/// <see cref="TileLayerPlan"/> draws place tiles, and an animated tile uses every frame of its animation.
/// </summary>
internal static class MapUses
{
    private const string MapsFolder = "Maps/";

    /// <summary>The maps whose tiles form the core terrain package; the farm, the village and the farmhouse where the first hour is played.</summary>
    private static readonly string[] s_coreMaps = [GameMaps.Farm, GameMaps.Town, GameMaps.FarmHouse];

    /// <summary>Adds the sheets and tiles of every hosted map to the ledger.</summary>
    /// <param name="ledger">The ledger.</param>
    /// <param name="assetsRoot">The <c>assets/</c> folder.</param>
    /// <returns>The map files and one tile report per file, with the mine's floor plans summed up as one.</returns>
    public static MapCatalog Collect(UseLedger ledger, string assetsRoot)
    {
        Dictionary<string, Package> files = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> ids = new(StringComparer.Ordinal);
        foreach (MapSpec spec in GameMaps.All)
        {
            Package package = Array.IndexOf(s_coreMaps, spec.Id) >= 0 ? Package.CoreTerrain : Package.RemainingMaps;
            if (!files.TryGetValue(spec.TmxFile, out Package known) || package < known)
            {
                files[spec.TmxFile] = package;
            }

            if (!ids.TryGetValue(spec.TmxFile, out List<string>? list))
            {
                list = [];
                ids.Add(spec.TmxFile, list);
            }

            list.Add(spec.Id);
        }

        List<string> ordered = [.. files.Keys];
        ordered.Sort(StringComparer.Ordinal);

        List<MapReport> reports = [];
        List<MapReport> mineFloors = [];
        List<MapFile> mapFiles = [];
        foreach (string tmx in ordered)
        {
            (MapReport report, MapFile file) = ReadMap(ledger, assetsRoot, tmx, files[tmx], ids[tmx]);
            mapFiles.Add(file);
            (tmx.StartsWith("Mines/", StringComparison.Ordinal) ? mineFloors : reports).Add(report);
        }

        if (mineFloors.Count > 0)
        {
            reports.Add(Sum("Mines/*.tmx", Package.RemainingMaps, mineFloors));
        }

        reports.Sort(static (a, b) => a.Package != b.Package ? a.Package.CompareTo(b.Package) : string.CompareOrdinal(a.Name, b.Name));
        return new MapCatalog(mapFiles, reports);
    }

    private static (MapReport Report, MapFile File) ReadMap(UseLedger ledger, string assetsRoot, string tmx, Package package, List<string> mapIds)
    {
        string path = Path.Combine(assetsRoot, MapsFolder + tmx);
        TileMap map = TmxReader.Parse(File.ReadAllText(path));
        string by = tmx.StartsWith("Mines/", StringComparison.Ordinal) ? MapsFolder + "Mines/*.tmx" : MapsFolder + tmx;

        // A sheet is named relative to the folder of the map file, as MapLoader resolves it.
        int slash = tmx.LastIndexOf('/');
        string folder = MapsFolder + (slash < 0 ? string.Empty : tmx[..(slash + 1)]);
        string[] sheetPaths = new string[map.TileSets.Count];
        for (int i = 0; i < sheetPaths.Length; i++)
        {
            string source = map.TileSets[i].ImageSource.Replace('\\', '/');
            sheetPaths[i] = folder + (source.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? source : source + ".png");
            _ = ledger.Loaded(sheetPaths[i], package, by);
        }

        Dictionary<(int Set, int Id), int[]> animations = [];
        List<LayerReport> layers = [];
        bool[] placed = new bool[map.Width * map.Height];
        TileLayerPlan plan = TileLayerPlan.For(map);
        foreach (TileLayer layer in plan.Ground)
        {
            layers.Add(ReadLayer(ledger, map, layer, sheetPaths, animations, package, by, placed));
        }

        foreach (TileLayer layer in plan.Front)
        {
            layers.Add(ReadLayer(ledger, map, layer, sheetPaths, animations, package, by, placed));
        }

        int count = 0;
        foreach (bool position in placed)
        {
            if (position)
            {
                count++;
            }
        }

        mapIds.Sort(StringComparer.Ordinal);
        return (new MapReport(tmx, package, 1, layers), new MapFile(MapsFolder + tmx, package, map.TileWidth, map.TileHeight, map.Width, map.Height, count, mapIds));
    }

    private static LayerReport ReadLayer(UseLedger ledger, TileMap map, TileLayer layer, string[] sheetPaths, Dictionary<(int Set, int Id), int[]> animations, Package package, string by, bool[] placed)
    {
        SortedDictionary<string, SortedSet<int>> sheets = new(StringComparer.Ordinal);
        HashSet<int> seen = [];
        for (int position = 0; position < layer.Gids.Length; position++)
        {
            int gid = layer.Gids[position];
            if (gid <= 0)
            {
                continue;
            }

            placed[position] = true;
            if (!seen.Add(gid))
            {
                continue;
            }

            int set = map.FindTileSet(gid);
            if (set < 0)
            {
                continue;
            }

            TileSet tileSet = map.TileSets[set];
            int local = gid - tileSet.FirstGid;
            FileEntry entry = ledger.Loaded(sheetPaths[set], package, by);
            if (!sheets.TryGetValue(sheetPaths[set], out SortedSet<int>? ids))
            {
                ids = [];
                sheets.Add(sheetPaths[set], ids);
            }

            _ = ids.Add(local);
            MarkTile(entry, tileSet, local, map.TileWidth, map.TileHeight, package, frame: false);
            if (!animations.TryGetValue((set, local), out int[]? frames))
            {
                frames = AnimationFrames(tileSet, local);
                animations.Add((set, local), frames);
            }

            foreach (int frame in frames)
            {
                _ = ids.Add(frame);
                MarkTile(entry, tileSet, frame, map.TileWidth, map.TileHeight, package, frame: true);
            }
        }

        return new LayerReport(layer.Name, sheets);
    }

    private static void MarkTile(FileEntry entry, TileSet tileSet, int local, int tileWidth, int tileHeight, Package package, bool frame)
        => entry.Mark(package, local % tileSet.Columns * tileWidth, local / tileSet.Columns * tileHeight, tileWidth, tileHeight, frame);

    private static int[] AnimationFrames(TileSet tileSet, int local)
    {
        TileAnimation? animation = tileSet.Tiles?.GetAnimation(local);
        if (animation is null)
        {
            return [];
        }

        // Walk one loop millisecond by millisecond; the animation hands back the tile shown at each instant.
        SortedSet<int> shown = [];
        for (int t = 0; t < animation.TotalMilliseconds; t++)
        {
            _ = shown.Add(animation.FrameAt(t));
        }

        return [.. shown];
    }

    private static MapReport Sum(string name, Package package, List<MapReport> parts)
    {
        List<string> order = [];
        Dictionary<string, SortedDictionary<string, SortedSet<int>>> merged = new(StringComparer.Ordinal);
        foreach (MapReport part in parts)
        {
            foreach (LayerReport layer in part.Layers)
            {
                if (!merged.TryGetValue(layer.Name, out SortedDictionary<string, SortedSet<int>>? sheets))
                {
                    sheets = new SortedDictionary<string, SortedSet<int>>(StringComparer.Ordinal);
                    merged.Add(layer.Name, sheets);
                    order.Add(layer.Name);
                }

                foreach ((string sheet, SortedSet<int> ids) in layer.Sheets)
                {
                    if (!sheets.TryGetValue(sheet, out SortedSet<int>? into))
                    {
                        into = [];
                        sheets.Add(sheet, into);
                    }

                    into.UnionWith(ids);
                }
            }
        }

        List<LayerReport> layers = [];
        foreach (string layer in order)
        {
            layers.Add(new LayerReport(layer, merged[layer]));
        }

        return new MapReport(name, package, parts.Count, layers);
    }
}
