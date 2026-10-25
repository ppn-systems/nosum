// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using Nosum.Core;

namespace Nosum.MapReauthor;

/// <summary>
/// Builds the own tilesheets the re-authored maps draw their buildings, trees, cliffs, fences, floors and walls from. An own sheet has the name,
/// the size and the grid of the placeholder sheet it replaces, and is served under the same name (<c>art/README.md</c>), so a map names the same
/// tile index at the same place: layers, tileset properties and animations of the placeholder map stay as they are. Only the cells some hosted map
/// (or a drawing routine of the client) places are drawn, by <see cref="ObjectPainter"/>; the other cells stay transparent.
/// </summary>
internal sealed class SheetBuilder
{
    // Cells the client reads from a sheet without a map: the bridge plank of the Beach and the top of the mine ladder.
    private static readonly (string Sheet, int Column, int Row, bool Ladder)[] s_clientCells =
    [
        ("spring_beach.png", 8, 4, false),
        ("Mines/mine.png", 3, 4, true),
    ];

    private readonly Dictionary<string, SortedDictionary<int, CellUse>> _cells = [];
    private readonly Dictionary<string, int> _columns = [];

    /// <summary>Notes the cells a re-authored map draws from its placeholder sheets (the own terrain tileset is left out).</summary>
    /// <param name="xml">The re-authored map's text.</param>
    /// <param name="mapFile">The map's file, relative to <c>assets/Maps</c>, for example <c>Mines/3.tmx</c>.</param>
    public void Add(string xml, string mapFile)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(mapFile);

        TileMap map = TmxReader.Parse(xml);
        string folder = mapFile.Contains('/', StringComparison.Ordinal) ? mapFile[..(mapFile.LastIndexOf('/') + 1)] : string.Empty;
        string[] keys = new string[map.TileSets.Count];
        for (int i = 0; i < keys.Length; i++)
        {
            string name = map.TileSets[i].ImageSource.Replace('\\', '/');
            keys[i] = folder + (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name : name + ".png");
            if (Reauthor.SheetName(name) != OwnTileset.Name)
            {
                _columns[keys[i]] = map.TileSets[i].Columns;
                _ = this.CellsOf(keys[i]);
            }
        }

        foreach (TileLayer layer in map.Layers)
        {
            if (layer.Name == "Paths")
            {
                continue;
            }

            bool ground = layer.Name.StartsWith("Back", StringComparison.Ordinal);
            foreach (int gid in layer.Gids)
            {
                int set = gid > 0 ? map.FindTileSet(gid) : -1;
                if (set < 0 || Reauthor.SheetName(map.TileSets[set].ImageSource) == OwnTileset.Name)
                {
                    continue;
                }

                TileSet tileSet = map.TileSets[set];
                int local = gid - tileSet.FirstGid;
                this.Use(keys[set], local, ground);
                if (tileSet.Tiles?.GetAnimation(local) is TileAnimation animation)
                {
                    this.UseFrames(keys[set], animation, ground);
                }
            }
        }
    }

    /// <summary>Draws every sheet that was noted.</summary>
    /// <param name="assetsMaps">The folder of the placeholder maps and sheets (<c>assets/Maps</c>).</param>
    /// <returns>The own sheets by their path relative to <c>assets/Maps</c>.</returns>
    public SortedDictionary<string, Rgba> Build(string assetsMaps)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetsMaps);

        foreach ((string sheet, int column, int row, bool _) in s_clientCells)
        {
            if (_cells.ContainsKey(sheet))
            {
                this.Use(sheet, (row * _columns[sheet]) + column, ground: false);
            }
        }

        SortedDictionary<string, Rgba> sheets = [];
        foreach ((string key, SortedDictionary<int, CellUse> cells) in _cells)
        {
            Rgba source = Rgba.Read(Path.Combine(assetsMaps, key));
            Rgba own = new(source.Width, source.Height);
            int columns = _columns[key];
            foreach ((int local, CellUse use) in cells)
            {
                int x = local % columns * OwnTileset.TileSize, y = local / columns * OwnTileset.TileSize;
                if (y + OwnTileset.TileSize > source.Height)
                {
                    continue;
                }

                if (Array.Exists(s_clientCells, c => c.Ladder && c.Sheet == key && ((c.Row * columns) + c.Column) == local))
                {
                    Ladder.Paint(own, x, y);
                }
                else
                {
                    ObjectPainter.Paint(source, x, y, own, x, y, local, use.Ground > use.Other, use.Phase);
                }
            }

            sheets[key] = own;
        }

        return sheets;
    }

    private SortedDictionary<int, CellUse> CellsOf(string key)
    {
        if (!_cells.TryGetValue(key, out SortedDictionary<int, CellUse>? cells))
        {
            cells = [];
            _cells[key] = cells;
        }

        return cells;
    }

    private void Use(string key, int local, bool ground)
    {
        SortedDictionary<int, CellUse> cells = this.CellsOf(key);
        CellUse use = cells.GetValueOrDefault(local) ?? new CellUse();
        if (ground)
        {
            use.Ground++;
        }
        else
        {
            use.Other++;
        }

        cells[local] = use;
    }

    private void UseFrames(string key, TileAnimation animation, bool ground)
    {
        // Frames are found by sampling the loop; each frame after the first shifts the ripples of its picture, so an animation stays a small own loop.
        SortedDictionary<int, CellUse> cells = this.CellsOf(key);
        List<int> seen = [];
        for (long t = 0; t < animation.TotalMilliseconds; t += 10)
        {
            int frame = animation.FrameAt(t);
            if (!seen.Contains(frame))
            {
                seen.Add(frame);
            }
        }

        for (int i = 0; i < seen.Count; i++)
        {
            CellUse use = cells.GetValueOrDefault(seen[i]) ?? new CellUse();
            if (use.Ground + use.Other == 0)
            {
                use.Phase = i;
                if (ground)
                {
                    use.Ground++;
                }
                else
                {
                    use.Other++;
                }
            }

            cells[seen[i]] = use;
        }
    }

    private sealed class CellUse
    {
        public int Ground { get; set; }

        public int Other { get; set; }

        public int Phase { get; set; }
    }
}
