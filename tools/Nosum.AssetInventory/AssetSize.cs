// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Nosum.Core;

namespace Nosum.AssetInventory;

/// <summary>The pixel size of an asset file: an image's, or a map's tiles × tile size.</summary>
internal static class AssetSize
{
    /// <summary>Reads the size of a <c>.png</c> or <c>.tmx</c> file.</summary>
    /// <param name="path">Path of the file.</param>
    /// <returns>The width and height in pixels.</returns>
    public static (int Width, int Height) Read(string path)
    {
        if (!path.EndsWith(".tmx", StringComparison.OrdinalIgnoreCase))
        {
            return PngHeader.ReadSize(path);
        }

        TileMap map = TmxReader.Parse(File.ReadAllText(path));
        return (map.Width * map.TileWidth, map.Height * map.TileHeight);
    }
}
