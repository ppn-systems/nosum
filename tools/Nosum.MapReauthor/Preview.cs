// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using Nosum.Core;

namespace Nosum.MapReauthor;

/// <summary>Draws the tile layers of a map to a PNG, to look at a re-authored map without starting the game. Animations are not played.</summary>
internal static class Preview
{
    /// <summary>Renders a map.</summary>
    /// <param name="tmxPath">The <c>.tmx</c> file.</param>
    /// <param name="sheetFolders">Root folders searched, in order, for the tileset images.</param>
    /// <param name="mapFolder">The folder of the map under its root (<c>Mines</c> for <c>Mines/3.tmx</c>, empty for the others); images are named relative to it.</param>
    /// <returns>The picture.</returns>
    public static Rgba Render(string tmxPath, IReadOnlyList<string> sheetFolders, string mapFolder = "")
    {
        TileMap map = TmxReader.Parse(File.ReadAllText(tmxPath));
        Rgba[] sheets = new Rgba[map.TileSets.Count];
        for (int i = 0; i < sheets.Length; i++)
        {
            string name = Path.Combine(mapFolder, map.TileSets[i].ImageSource.Replace('\\', '/') + (map.TileSets[i].ImageSource.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? string.Empty : ".png"));
            string? found = null;
            foreach (string folder in sheetFolders)
            {
                string candidate = Path.GetFullPath(Path.Combine(folder, name));
                if (File.Exists(candidate))
                {
                    found = candidate;
                    break;
                }
            }

            sheets[i] = Rgba.Read(found ?? throw new FileNotFoundException($"Tilesheet {name} not found.", name));
        }

        int tile = map.TileWidth;
        Rgba picture = new(map.Width * tile, map.Height * tile);
        foreach (TileLayer layer in map.Layers)
        {
            if (layer.Name == "Paths")
            {
                continue;
            }

            for (int i = 0; i < layer.Gids.Length; i++)
            {
                int gid = layer.Gids[i];
                int set = map.FindTileSet(gid);
                if (gid <= 0 || set < 0)
                {
                    continue;
                }

                TileSet tileSet = map.TileSets[set];
                int local = gid - tileSet.FirstGid;
                picture.Over(sheets[set], local % tileSet.Columns * tile, local / tileSet.Columns * tile, tile, i % map.Width * tile, i / map.Width * tile);
            }
        }

        return picture;
    }
}
