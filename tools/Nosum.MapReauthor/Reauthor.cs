// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Nosum.Core;

namespace Nosum.MapReauthor;

/// <summary>A placeholder tile of the ground layer that stays on its placeholder sheet.</summary>
/// <param name="Sheet">The tileset image name without extension.</param>
/// <param name="Index">The local tile index.</param>
/// <param name="Cells">How many cells use it.</param>
/// <param name="Reason">Why it was kept.</param>
internal sealed record KeptTile(string Sheet, int Index, int Cells, string Reason);

/// <summary>The outcome of re-authoring one map.</summary>
/// <param name="Tmx">The own map file text.</param>
/// <param name="Report">The markdown report.</param>
/// <param name="TerrainCells">Cells of the ground layer that place a tile.</param>
/// <param name="OwnCells">Of those, the cells that now draw an own tile.</param>
/// <param name="Kept">The placeholder tiles that stay, with their cell counts.</param>
internal sealed record ReauthorResult(string Tmx, string Report, int TerrainCells, int OwnCells, IReadOnlyList<KeptTile> Kept);

/// <summary>
/// Re-authors a placeholder map onto <see cref="OwnTileset"/>: every tile of the ground layer (<c>Back</c>) is classified into a
/// <see cref="TerrainKind"/> by <see cref="KindTable"/> and replaced by an own fill of that kind; where a kind meets a lower one, the cell
/// also gets a rounded edge piece on a new layer, <c>BackEdge</c>, right above <c>Back</c>. Every other layer and object group is copied
/// as it is, character for character, so collision, exits, spawn tiles and node sites do not change.
/// The placeholder tilesets stay in the file with their first ids, so the tiles that are kept (and every other layer) still resolve.
/// </summary>
internal static partial class Reauthor
{
    private const string GroundLayer = "Back";
    private const string EdgeLayer = "BackEdge";

    /// <summary>Re-authors a map.</summary>
    /// <param name="xml">The placeholder <c>.tmx</c> text.</param>
    /// <param name="mapFile">The map's file name, for the report.</param>
    /// <param name="table">The classification table.</param>
    /// <returns>The own map and its report.</returns>
    /// <exception cref="InvalidDataException">The map has no ground layer or its layout is not the expected one.</exception>
    public static ReauthorResult Run(string xml, string mapFile, KindTable table)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(table);

        TileMap map = TmxReader.Parse(xml);
        TileLayer ground = map.FindLayer(GroundLayer) ?? throw new InvalidDataException($"{mapFile} has no {GroundLayer} layer.");
        int width = map.Width, height = map.Height;

        TerrainKind[] kinds = new TerrainKind[width * height];
        Dictionary<(string, int), (int Cells, string Reason)> kept = [];
        int terrain = 0;
        for (int i = 0; i < kinds.Length; i++)
        {
            int gid = ground.Gids[i];
            if (gid <= 0)
            {
                continue;
            }

            terrain++;
            int set = map.FindTileSet(gid);
            if (set < 0)
            {
                continue;
            }

            string sheet = SheetName(map.TileSets[set].ImageSource);
            int local = gid - map.TileSets[set].FirstGid;
            string? reason = null;
            if (!table.TryGet(sheet, local, out TerrainKind kind))
            {
                reason = "not in the kind table";
            }
            else if (kind == TerrainKind.Other)
            {
                reason = "kind other (not terrain)";
            }
            else if (TerrainKinds.Priority(kind) < 0)
            {
                reason = $"no own {kind.ToString().ToLowerInvariant()} set yet";
            }
            else if (OwnTileset.Fill(kind, map.GetTraits(gid), 0) < 0)
            {
                reason = $"no own {kind.ToString().ToLowerInvariant()} fill with traits {map.GetTraits(gid)}";
            }
            else
            {
                kinds[i] = kind;
            }

            if (reason is not null)
            {
                kept[(sheet, local)] = (kept.GetValueOrDefault((sheet, local)).Cells + 1, reason);
            }
        }

        int ownFirstGid = map.TileSets[^1].FirstGid + map.TileSets[^1].TileCount;
        int[] corner = TerrainEdges.Resolve(kinds, width, height);
        int[] back = new int[kinds.Length];
        int[] edge = new int[kinds.Length];
        int own = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                if (TerrainKinds.Priority(kinds[i]) < 0)
                {
                    continue;
                }

                own++;
                back[i] = ownFirstGid + OwnTileset.Fill(kinds[i], map.GetTraits(ground.Gids[i]), Variant(x, y));
                edge[i] = corner[i] < 0 ? 0 : ownFirstGid + corner[i];
            }
        }

        string tmx = Rewrite(xml, back, edge, width, height, ownFirstGid, mapFile);
        List<KeptTile> list = [];
        foreach (((string sheet, int index), (int cells, string reason)) in kept)
        {
            list.Add(new KeptTile(sheet, index, cells, reason));
        }

        list.Sort(static (a, b) => b.Cells != a.Cells ? b.Cells.CompareTo(a.Cells) : string.CompareOrdinal(a.Sheet + a.Index.ToString("D5", CultureInfo.InvariantCulture), b.Sheet + b.Index.ToString("D5", CultureInfo.InvariantCulture)));
        return new ReauthorResult(tmx, Report(mapFile, map, terrain, own, list), terrain, own, list);
    }

    /// <summary>Returns the tileset image name of a tileset without its extension.</summary>
    /// <param name="imageSource">The <c>source</c> attribute of the tileset image.</param>
    /// <returns>The name.</returns>
    public static string SheetName(string imageSource)
    {
        ArgumentNullException.ThrowIfNull(imageSource);

        string name = imageSource.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        return name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    // A fixed scatter of the four texture variants, so the same map always gets the same ground.
    private static int Variant(int x, int y)
    {
        uint h = unchecked(((uint)x * 374761393u) + ((uint)y * 668265263u));
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        return (int)((h ^ (h >> 16)) & 3);
    }

    private static string Rewrite(string xml, int[] back, int[] edge, int width, int height, int ownFirstGid, string mapFile)
    {
        Match layer = GroundLayerBlock().Match(xml);
        if (!layer.Success)
        {
            throw new InvalidDataException($"{mapFile}: the {GroundLayer} layer does not have the expected layout.");
        }

        int firstLayer = xml.IndexOf("\n  <layer ", StringComparison.Ordinal) + 1;
        Match nextId = NextLayerId().Match(xml);
        if (firstLayer <= 0 || !nextId.Success)
        {
            throw new InvalidDataException($"{mapFile}: no tile layer or no nextlayerid.");
        }

        int id = int.Parse(nextId.Groups[1].Value, CultureInfo.InvariantCulture);
        string newLayer = $"  <layer id=\"{id}\" name=\"{EdgeLayer}\" width=\"{width}\" height=\"{height}\" opacity=\"1\" offsetx=\"0\" offsety=\"0\">\n    <properties />\n    <data encoding=\"csv\">\n{Csv(edge, width, height)}\n</data>\n  </layer>\n";

        // A tile that stays keeps its raw value (it may carry a flip flag), so the layer is rewritten from the file's own numbers.
        string[] raw = layer.Groups["data"].Value.Split([',', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries);
        int[] merged = new int[back.Length];
        for (int i = 0; i < merged.Length; i++)
        {
            merged[i] = back[i] != 0 ? back[i] : int.MinValue;
        }

        StringBuilder rows = new();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                _ = rows.Append(merged[i] != int.MinValue ? merged[i].ToString(CultureInfo.InvariantCulture) : raw[i]);
                if (x < width - 1)
                {
                    _ = rows.Append(',');
                }
            }

            if (y < height - 1)
            {
                _ = rows.Append(",\n");
            }
        }

        Group data = layer.Groups["data"];
        int layerEnd = layer.Index + layer.Length;
        StringBuilder result = new();
        _ = result.Append(xml, 0, firstLayer)
            .Append(OwnTileset.TilesetElement(ownFirstGid))
            .Append(xml, firstLayer, data.Index - firstLayer)
            .Append('\n').Append(rows).Append('\n')
            .Append(xml, data.Index + data.Length, layerEnd - data.Index - data.Length)
            .Append(newLayer)
            .Append(xml, layerEnd, xml.Length - layerEnd);
        return NextLayerId().Replace(result.ToString(), $"nextlayerid=\"{id + 1}\"", 1);
    }

    private static string Csv(int[] gids, int width, int height)
    {
        StringBuilder text = new();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                _ = text.Append(gids[(y * width) + x]);
                if (x < width - 1)
                {
                    _ = text.Append(',');
                }
            }

            if (y < height - 1)
            {
                _ = text.Append(",\n");
            }
        }

        return text.ToString();
    }

    private static string Report(string mapFile, TileMap map, int terrain, int own, List<KeptTile> kept)
    {
        StringBuilder text = new();
        _ = text.Append(CultureInfo.InvariantCulture, $"# Re-authored `{mapFile}`\n\n");
        _ = text.Append("Written by `tools/Nosum.MapReauthor`; do not edit by hand.\n\n");
        _ = text.Append(CultureInfo.InvariantCulture, $"- Ground cells (`{GroundLayer}` layer, {map.Width} × {map.Height}): {terrain:N0} place a tile.\n");
        _ = text.Append(CultureInfo.InvariantCulture, $"- Cells now drawn from the own terrain tileset: {own:N0} ({100.0 * own / Math.Max(1, terrain):F1} %).\n");
        _ = text.Append(CultureInfo.InvariantCulture, $"- Cells that stay on a placeholder tile: {terrain - own:N0} ({kept.Count} different tiles).\n");
        _ = text.Append("- Every other layer (collision, logic and the drawn building, tree, fence and front layers) is copied unchanged and still draws placeholder tiles.\n\n");
        if (kept.Count > 0)
        {
            _ = text.Append("| Sheet | Tile | Cells | Why it stays |\n| :-- | --: | --: | :-- |\n");
            foreach (KeptTile tile in kept)
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"| `{tile.Sheet}` | {tile.Index} | {tile.Cells} | {tile.Reason} |\n");
            }
        }

        return text.ToString();
    }

    [GeneratedRegex("<layer id=\"\\d+\" name=\"Back\"[^>]*>\\s*(?:<properties />\\s*)?<data encoding=\"csv\">(?<data>.*?)</data>\\s*</layer>\\n", RegexOptions.Singleline)]
    private static partial Regex GroundLayerBlock();

    [GeneratedRegex("nextlayerid=\"(\\d+)\"")]
    private static partial Regex NextLayerId();
}
