// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nosum.MapGen.Sheets;

/// <summary>One drawn sheet: its tileset name, its picture and what the picture draws.</summary>
/// <param name="Name">The picture's base name: the file is <c>art/own/Maps/&lt;Name&gt;.png</c>.</param>
/// <param name="TileSet">The tileset name the maps use; the trait file is <c>art/own/source/tilesets/&lt;TileSet&gt;.tileset</c>.</param>
/// <param name="TileCount">The number of tiles the sheet defines.</param>
/// <param name="Draw">Draws the sheet into a picture of the right size.</param>
internal sealed record SheetInfo(string Name, string TileSet, int TileCount, Action<Canvas> Draw)
{
    /// <summary>Tiles per row of every sheet.</summary>
    public const int Columns = 16;

    /// <summary>Side of a tile in pixels.</summary>
    public const int TileSize = 16;

    /// <summary>Gets the rows of the picture: the tile count over the columns, rounded up.</summary>
    public int Rows => (this.TileCount + Columns - 1) / Columns;

    /// <summary>Gets the picture's width in pixels.</summary>
    public int Width => Columns * TileSize;

    /// <summary>Gets the picture's height in pixels.</summary>
    public int Height => this.Rows * TileSize;

    /// <summary>Gets the picture's file name.</summary>
    public string File => this.Name + ".png";

    /// <summary>Draws the sheet.</summary>
    /// <returns>The picture.</returns>
    public Canvas Render()
    {
        Canvas canvas = new(this.Width, this.Height);
        this.Draw(canvas);
        return canvas;
    }
}

/// <summary>
/// Nosum's own tilesheets, drawn in code (docs/adr/ADR-042 P2). <c>sheets</c> writes each PNG under <c>art/own/Maps</c>; <c>sheets --check</c>
/// fails when a committed PNG is not what the drawing gives. The drawing is deterministic, so the same code always writes the same bytes.
/// </summary>
internal static class SheetCatalog
{
    /// <summary>Gets the sheets, in the order of the contact sheet.</summary>
    public static IReadOnlyList<SheetInfo> All { get; } =
    [
        new("cave", "cave", CaveSheet.TileCount, CaveSheet.Draw),
        new("terrain", "terrain", TerrainSheet.TileCount, TerrainSheet.Draw),
        new("nature", "nature", NatureSheet.TileCount, NatureSheet.Draw),
        new("town", "town", TownSheet.TileCount, TownSheet.Draw),
        new("interior", "interior", InteriorSheet.TileCount, InteriorSheet.Draw),
        new("markers", "Paths", MarkerSheet.TileCount, MarkerSheet.Draw),
    ];

    /// <summary>Returns the sheet of a tileset name, or <see langword="null"/> when no sheet of this tool has it.</summary>
    /// <param name="tileSet">The tileset name.</param>
    /// <returns>The sheet.</returns>
    public static SheetInfo? ByTileSet(string tileSet) => All.FirstOrDefault(s => string.Equals(s.TileSet, tileSet, StringComparison.Ordinal));

    /// <summary>Returns the names of the sheets whose committed picture differs from the drawing, or is missing.</summary>
    /// <param name="maps">The folder of the pictures (<c>art/own/Maps</c>).</param>
    /// <returns>The file names that differ; empty when every committed picture is current.</returns>
    public static IReadOnlyList<string> Differences(string maps)
    {
        List<string> stale = [];
        foreach (SheetInfo sheet in All)
        {
            string path = Path.Combine(maps, sheet.File);
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(sheet.Render().ToPng()))
            {
                stale.Add(sheet.File);
            }
        }

        return stale;
    }

    /// <summary>Writes every sheet's picture into a folder.</summary>
    /// <param name="maps">The folder of the pictures.</param>
    public static void Write(string maps)
    {
        Directory.CreateDirectory(maps);
        foreach (SheetInfo sheet in All)
        {
            File.WriteAllBytes(Path.Combine(maps, sheet.File), sheet.Render().ToPng());
        }
    }

    /// <summary>Draws every sheet, enlarged, one below the other, on a dark ground: a picture to judge the art by.</summary>
    /// <param name="scale">The enlargement of each pixel.</param>
    /// <returns>The picture.</returns>
    public static Canvas Contact(int scale)
    {
        const int Gap = 12;
        List<Canvas> pictures = All.Select(s => s.Render()).ToList();
        int width = (pictures.Max(p => p.Width) * scale) + (2 * Gap);
        int height = (pictures.Sum(p => p.Height) * scale) + ((pictures.Count + 1) * Gap);
        Canvas contact = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                contact.Set(x, y, new Rgb(40, 34, 48));
            }
        }

        int top = Gap;
        foreach (Canvas picture in pictures)
        {
            contact.Paste(picture, Gap, top, scale);
            top += (picture.Height * scale) + Gap;
        }

        return contact;
    }
}
