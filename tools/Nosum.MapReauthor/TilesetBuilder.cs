// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Nosum.Core;

namespace Nosum.MapReauthor;

/// <summary>
/// Composes <c>own_terrain.png</c> from the PixelLab corner tilesets in <c>art/own/source/pixellab/</c> (<see cref="WangSet"/>).
/// The fills are the uniform tiles of the sets that really show their terrain (several sets carry a flat brown placeholder tile instead of grass),
/// mirrored for texture variants; the corner tiles are copied as they are. The grass of every set is shifted to the mean grass colour of the
/// grass/dirt set, because each set was generated with a slightly different green and the tiles of different sets meet on a map.
/// </summary>
internal static class TilesetBuilder
{
    // Where the uniform tile of a kind is: a set, and which of its corner words the kind has there. Only sets that draw the terrain for real are listed.
    private static readonly Dictionary<TerrainKind, (string Set, string Word)> s_fillSources = new()
    {
        [TerrainKind.Grass] = ("grass_dirt", "upper"),
        [TerrainKind.Dirt] = ("grass_dirt", "lower"),
        [TerrainKind.Water] = ("water_grass", "lower"),
        [TerrainKind.Sand] = ("sand_grass", "lower"),
    };

    /// <summary>Builds the tileset image.</summary>
    /// <param name="sourceFolder">The folder with the sets' images and metadata (<c>art/own/source/pixellab</c>).</param>
    /// <returns>The tileset pixels.</returns>
    /// <exception cref="InvalidDataException">A set file is missing or its metadata does not describe 16 corner tiles.</exception>
    public static Rgba Build(string sourceFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceFolder);

        Dictionary<string, Rgba> sheets = [];
        Dictionary<string, List<SourceTile>> tiles = [];
        foreach (WangSet set in WangSet.All)
        {
            sheets[set.Name] = Rgba.Read(Path.Combine(sourceFolder, set.Name + ".png"));
            tiles[set.Name] = ReadTiles(Path.Combine(sourceFolder, set.Name + ".json"));
        }

        (double r, double g, double b) = GrassMean(sheets["grass_dirt"]);
        foreach (Rgba sheet in sheets.Values)
        {
            ShiftGrass(sheet, r, g, b);
        }

        Rgba image = new(OwnTileset.Columns * OwnTileset.TileSize, OwnTileset.Rows * OwnTileset.TileSize);
        foreach (TerrainKind kind in TerrainKinds.Fills)
        {
            (string setName, string word) = s_fillSources[kind];
            SourceTile source = tiles[setName].Find(t => t.Uniform == word)
                ?? throw new InvalidDataException($"{setName}: no uniform '{word}' tile.");
            foreach (TileTraits traits in OwnTileset.TraitsOf(kind))
            {
                for (int v = 0; v < OwnTileset.Variants; v++)
                {
                    int tile = OwnTileset.Fill(kind, traits, v);
                    int left = tile % OwnTileset.Columns * OwnTileset.TileSize, top = tile / OwnTileset.Columns * OwnTileset.TileSize;
                    image.Blit(sheets[setName], source.X, source.Y, OwnTileset.TileSize, left, top, (v & 1) != 0, flipY: false);
                    if (kind == TerrainKind.Water && v >= 2)
                    {
                        Calm(image, left, top);
                    }
                }
            }
        }

        for (int s = 0; s < WangSet.All.Length; s++)
        {
            WangSet set = WangSet.All[s];
            bool[] seen = new bool[WangSet.Tiles];
            foreach (SourceTile source in tiles[set.Name])
            {
                // A uniform tile is never drawn on the edge layer (the fill shows instead), and some sets carry a placeholder there:
                // fill its slot with the real fill of that corner's kind.
                if (source.Uniform is { } word)
                {
                    (string fillSet, string fillWord) = s_fillSources[word == "lower" ? set.Lower : set.Upper];
                    SourceTile fill = tiles[fillSet].Find(t => t.Uniform == fillWord)!;
                    PutTile(image, OwnTileset.Corner(s, source.Code), sheets[fillSet], fill.X, fill.Y);
                }
                else
                {
                    PutTile(image, OwnTileset.Corner(s, source.Code), sheets[set.Name], source.X, source.Y);
                }

                seen[source.Code] = true;
            }

            if (Array.IndexOf(seen, false) >= 0)
            {
                throw new InvalidDataException($"{set.Name}: the metadata does not list all 16 corner combinations.");
            }
        }

        return image;
    }

    // The water tile has one swirl; repeated on every tile it reads as wallpaper, so half of the variants lose the swirl: every pixel lighter than the
    // tile's most common colour becomes that colour.
    private static void Calm(Rgba image, int left, int top)
    {
        Dictionary<int, int> counts = [];
        int size = OwnTileset.TileSize;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (((top + y) * image.Width) + left + x) * 4;
                int key = (image.Pixels[i] << 16) | (image.Pixels[i + 1] << 8) | image.Pixels[i + 2];
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        int common = 0, best = -1;
        foreach ((int key, int n) in counts)
        {
            if (n > best || (n == best && key < common))
            {
                (common, best) = (key, n);
            }
        }

        int brightness = ((common >> 16) & 0xFF) + ((common >> 8) & 0xFF) + (common & 0xFF);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (((top + y) * image.Width) + left + x) * 4;
                if (image.Pixels[i] + image.Pixels[i + 1] + image.Pixels[i + 2] > brightness)
                {
                    image.Pixels[i] = (byte)(common >> 16);
                    image.Pixels[i + 1] = (byte)(common >> 8);
                    image.Pixels[i + 2] = (byte)common;
                }
            }
        }
    }

    private static void PutTile(Rgba image, int tile, Rgba from, int x, int y)
        => image.Blit(from, x, y, OwnTileset.TileSize, tile % OwnTileset.Columns * OwnTileset.TileSize, tile / OwnTileset.Columns * OwnTileset.TileSize);

    private static List<SourceTile> ReadTiles(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        List<SourceTile> list = [];
        foreach (JsonElement tile in document.RootElement.GetProperty("tiles").EnumerateArray())
        {
            JsonElement corners = tile.GetProperty("corners");
            string nw = corners.GetProperty("NW").GetString()!, ne = corners.GetProperty("NE").GetString()!, sw = corners.GetProperty("SW").GetString()!, se = corners.GetProperty("SE").GetString()!;
            JsonElement box = tile.GetProperty("bounding_box");
            if (box.GetProperty("width").GetInt32() != OwnTileset.TileSize || box.GetProperty("height").GetInt32() != OwnTileset.TileSize)
            {
                throw new InvalidDataException($"{path}: a tile is not {OwnTileset.TileSize} px.");
            }

            string? uniform = nw == ne && ne == sw && sw == se ? nw : null;
            list.Add(new SourceTile(WangSet.CodeOf(nw, ne, sw, se), uniform, box.GetProperty("x").GetInt32(), box.GetProperty("y").GetInt32()));
        }

        return list;
    }

    // The bright green pixels (grass, not the dark outline or water): the grass of a set, to be moved onto one common colour.
    private static bool IsGrass(byte r, byte g, byte b, byte a) => a > 0 && g > r + 15 && g > b + 40 && g > 110;

    private static (double R, double G, double B) GrassMean(Rgba sheet)
    {
        double r = 0, g = 0, b = 0;
        int n = 0;
        for (int i = 0; i < sheet.Pixels.Length; i += 4)
        {
            if (IsGrass(sheet.Pixels[i], sheet.Pixels[i + 1], sheet.Pixels[i + 2], sheet.Pixels[i + 3]))
            {
                r += sheet.Pixels[i];
                g += sheet.Pixels[i + 1];
                b += sheet.Pixels[i + 2];
                n++;
            }
        }

        return n == 0 ? (0, 0, 0) : (r / n, g / n, b / n);
    }

    private static void ShiftGrass(Rgba sheet, double r, double g, double b)
    {
        (double mr, double mg, double mb) = GrassMean(sheet);
        for (int i = 0; i < sheet.Pixels.Length; i += 4)
        {
            if (IsGrass(sheet.Pixels[i], sheet.Pixels[i + 1], sheet.Pixels[i + 2], sheet.Pixels[i + 3]))
            {
                sheet.Pixels[i] = Clamp(sheet.Pixels[i] + (r - mr));
                sheet.Pixels[i + 1] = Clamp(sheet.Pixels[i + 1] + (g - mg));
                sheet.Pixels[i + 2] = Clamp(sheet.Pixels[i + 2] + (b - mb));
            }
        }
    }

    private static byte Clamp(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private sealed record SourceTile(int Code, string? Uniform, int X, int Y);
}
