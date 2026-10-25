// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Core;

namespace Nosum.MapReauthor;

/// <summary>
/// Composes <c>own_terrain.png</c> from the Kenney "Roguelike/RPG Pack" spritesheet (CC0): its flat fills and its rounded terrain blocks.
/// The fills of grass, dirt, sand and rock are the centre tile of their block, so a fill and an edge piece of one kind share a texture.
/// An edge piece for any neighbour mask is made from the block's nine slice pieces and its four inner-corner pieces: the pieces are
/// intersected (a pixel is drawn only where every piece draws it) and the pixel takes the colour of the piece whose rim is nearest to it.
/// </summary>
internal static class TilesetBuilder
{
    /// <summary>Distance between two tiles on the pack's sheet (16 px and a 1 px margin).</summary>
    private const int Pitch = 17;

    private const int RimReach = 3;

    // Where each rounded block starts on the sheet: the 3 x 3 slice and the 2 x 2 inner corners, as (column, row) of the tile grid.
    private static readonly Dictionary<TerrainKind, (int SliceX, int SliceY, int NotchX, int NotchY)> s_blocks = new()
    {
        [TerrainKind.Dirt] = (7, 9, 5, 9),
        [TerrainKind.Cliff] = (7, 15, 5, 15),
        [TerrainKind.Grass] = (2, 15, 0, 15),
        [TerrainKind.Sand] = (7, 21, 5, 21),
    };

    // Flat fills the pack has no rounded block for: tiles on the sheet, two textures each.
    private static readonly Dictionary<TerrainKind, (int X, int Y)[]> s_flat = new()
    {
        [TerrainKind.Water] = [(0, 0), (1, 0)],
        [TerrainKind.Path] = [(6, 2), (6, 3)],
    };

    /// <summary>Builds the tileset image.</summary>
    /// <param name="pack">The pack's <c>roguelikeSheet_transparent.png</c>.</param>
    /// <returns>The tileset pixels.</returns>
    public static Rgba Build(Rgba pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        Rgba image = new(OwnTileset.Columns * OwnTileset.TileSize, OwnTileset.Rows * OwnTileset.TileSize);
        foreach (TerrainKind kind in TerrainKinds.Fills)
        {
            foreach (TileTraits traits in OwnTileset.TraitsOf(kind))
            {
                for (int v = 0; v < OwnTileset.Variants; v++)
                {
                    PutFill(pack, image, kind, OwnTileset.Fill(kind, traits, v), v);
                }
            }
        }

        foreach (TerrainKind kind in TerrainKinds.Rounded)
        {
            for (int i = 0; i < Blob.Count; i++)
            {
                int tile = OwnTileset.Overlay(kind, Blob.MaskAt(i));
                Rgba piece = EdgePiece(pack, kind, Blob.MaskAt(i));
                image.Blit(piece, 0, 0, OwnTileset.TileSize, (tile % OwnTileset.Columns) * OwnTileset.TileSize, (tile / OwnTileset.Columns) * OwnTileset.TileSize);
            }
        }

        return image;
    }

    private static void PutFill(Rgba pack, Rgba image, TerrainKind kind, int tile, int variant)
    {
        (int sx, int sy, bool flipX, bool flipY) = FillSource(kind, variant);
        image.Blit(pack, sx * Pitch, sy * Pitch, OwnTileset.TileSize, (tile % OwnTileset.Columns) * OwnTileset.TileSize, (tile / OwnTileset.Columns) * OwnTileset.TileSize, flipX, flipY);
    }

    private static (int X, int Y, bool FlipX, bool FlipY) FillSource(TerrainKind kind, int variant)
    {
        if (s_flat.TryGetValue(kind, out (int X, int Y)[]? flat))
        {
            (int x, int y) = flat[variant & 1];
            return (x, y, variant >= 2, false);
        }

        (int sliceX, int sliceY, _, _) = s_blocks[kind];
        return (sliceX + 1, sliceY + 1, (variant & 1) != 0, (variant & 2) != 0);
    }

    private static Rgba EdgePiece(Rgba pack, TerrainKind kind, int mask)
    {
        (int sliceX, int sliceY, int notchX, int notchY) = s_blocks[kind];
        List<(int X, int Y)> pieces = [];

        // Sides that are outside: two adjacent ones use the rounded corner piece, a lone one the straight edge piece.
        bool north = (mask & Blob.N) == 0, east = (mask & Blob.E) == 0, south = (mask & Blob.S) == 0, west = (mask & Blob.W) == 0;
        bool northCovered = false, eastCovered = false, southCovered = false, westCovered = false;
        if (north && east)
        {
            pieces.Add((sliceX + 2, sliceY));
            northCovered = eastCovered = true;
        }

        if (east && south)
        {
            pieces.Add((sliceX + 2, sliceY + 2));
            eastCovered = southCovered = true;
        }

        if (south && west)
        {
            pieces.Add((sliceX, sliceY + 2));
            southCovered = westCovered = true;
        }

        if (west && north)
        {
            pieces.Add((sliceX, sliceY));
            westCovered = northCovered = true;
        }

        if (north && !northCovered)
        {
            pieces.Add((sliceX + 1, sliceY));
        }

        if (east && !eastCovered)
        {
            pieces.Add((sliceX + 2, sliceY + 1));
        }

        if (south && !southCovered)
        {
            pieces.Add((sliceX + 1, sliceY + 2));
        }

        if (west && !westCovered)
        {
            pieces.Add((sliceX, sliceY + 1));
        }

        // Inner corners: a corner outside while both of its sides are inside. The notch tiles are laid out SE, SW / NE, NW.
        if (!north && !east && (mask & Blob.NE) == 0)
        {
            pieces.Add((notchX, notchY + 1));
        }

        if (!south && !east && (mask & Blob.SE) == 0)
        {
            pieces.Add((notchX, notchY));
        }

        if (!south && !west && (mask & Blob.SW) == 0)
        {
            pieces.Add((notchX + 1, notchY));
        }

        if (!north && !west && (mask & Blob.NW) == 0)
        {
            pieces.Add((notchX + 1, notchY + 1));
        }

        if (pieces.Count == 0)
        {
            pieces.Add((sliceX + 1, sliceY + 1));
        }

        return Intersect(pack, pieces);
    }

    private static Rgba Intersect(Rgba pack, List<(int X, int Y)> pieces)
    {
        int size = OwnTileset.TileSize;
        int[][] reach = new int[pieces.Count][];
        for (int p = 0; p < pieces.Count; p++)
        {
            reach[p] = RimDistances(pack, pieces[p].X * Pitch, pieces[p].Y * Pitch);
        }

        Rgba result = new(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int best = -1;
                bool drawn = true;
                for (int p = 0; p < pieces.Count; p++)
                {
                    int d = reach[p][(y * size) + x];
                    if (d == 0)
                    {
                        drawn = false;
                        break;
                    }

                    if (best < 0 || d < reach[best][(y * size) + x])
                    {
                        best = p;
                    }
                }

                if (drawn)
                {
                    int from = ((((pieces[best].Y * Pitch) + y) * pack.Width) + (pieces[best].X * Pitch) + x) * 4;
                    Array.Copy(pack.Pixels, from, result.Pixels, ((y * size) + x) * 4, 4);
                }
            }
        }

        return result;
    }

    // Per pixel of a piece: 0 where it is transparent, else 1 + the distance to the nearest transparent pixel of the piece (up to RimReach).
    private static int[] RimDistances(Rgba pack, int left, int top)
    {
        int size = OwnTileset.TileSize;
        int[] result = new int[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (pack.Pixels[((((top + y) * pack.Width) + left + x) * 4) + 3] < 128)
                {
                    result[(y * size) + x] = 0;
                    continue;
                }

                int nearest = RimReach + 1;
                for (int dy = -RimReach; dy <= RimReach; dy++)
                {
                    for (int dx = -RimReach; dx <= RimReach; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= size || ny >= size)
                        {
                            continue;
                        }

                        if (pack.Pixels[((((top + ny) * pack.Width) + left + nx) * 4) + 3] < 128)
                        {
                            nearest = Math.Min(nearest, Math.Max(Math.Abs(dx), Math.Abs(dy)));
                        }
                    }
                }

                result[(y * size) + x] = nearest;
            }
        }

        return result;
    }
}
