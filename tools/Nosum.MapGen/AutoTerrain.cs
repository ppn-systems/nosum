// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.MapGen.Sheets;

namespace Nosum.MapGen;

/// <summary>
/// The terrain pass of the layout compiler. It varies the grass and dirt fills of the <c>Back</c> layer by a hash of the cell's position, so a
/// meadow does not read as one tile repeated, and it adds a ground layer <c>Back2</c> with the edge tile of every cell whose corners border a terrain
/// the terrain sheet has a transition for (shores, grass over dirt, sand and cobbles). The fills stay on <c>Back</c> and keep their traits; the edge
/// tiles carry none, so what a player may walk on does not change.
/// </summary>
internal static class AutoTerrain
{
    /// <summary>The name of the layer the edge tiles go on.</summary>
    public const string EdgeLayer = "Back2";

    private const string GroundLayer = "Back";
    private const string TerrainSet = "terrain";

    // Which terrain wins a corner when several border it: the earlier in this list.
    private static readonly TerrainSheet.Ground[] s_rank =
    [
        TerrainSheet.Ground.Grass,
        TerrainSheet.Ground.Sand,
        TerrainSheet.Ground.Shallow,
        TerrainSheet.Ground.Path,
        TerrainSheet.Ground.Dirt,
        TerrainSheet.Ground.Cliff,
        TerrainSheet.Ground.Deep,
    ];

    /// <summary>Varies the grass and dirt fills and adds the edge layer, in place.</summary>
    /// <param name="width">Map width in tiles.</param>
    /// <param name="height">Map height in tiles.</param>
    /// <param name="tilesets">The tilesets of the map.</param>
    /// <param name="layers">The layers, bottom first; <c>Back2</c> is inserted after <c>Back</c> when there are edges.</param>
    public static void Apply(int width, int height, IReadOnlyList<PlacedTileset> tilesets, List<MapLayer> layers)
    {
        PlacedTileset? terrain = null;
        foreach (PlacedTileset placed in tilesets)
        {
            if (string.Equals(placed.Set.Name, TerrainSet, StringComparison.Ordinal))
            {
                terrain = placed;
            }
        }

        int back = layers.FindIndex(l => string.Equals(l.Name, GroundLayer, StringComparison.Ordinal));
        if (terrain is null || back < 0 || layers.Exists(l => string.Equals(l.Name, EdgeLayer, StringComparison.Ordinal)))
        {
            return;
        }

        int[] gids = layers[back].Gids;
        int first = terrain.FirstGid;
        int[] kind = new int[width * height];
        for (int i = 0; i < gids.Length; i++)
        {
            int index = gids[i] - first;
            kind[i] = gids[i] > 0 && index >= 0 && index < TerrainSheet.FirstTransition ? index / TerrainSheet.Variants : -1;
        }

        // The variants: a cell of grass or dirt takes one of the kind's four fills by a hash of its position.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                if (kind[i] == (int)TerrainSheet.Ground.Grass || kind[i] == (int)TerrainSheet.Ground.Dirt)
                {
                    gids[i] = first + TerrainSheet.Fill((TerrainSheet.Ground)kind[i], (int)(Noise.Of(x, y, 77) >> 8) % TerrainSheet.Variants);
                }
            }
        }

        int[] edges = new int[width * height];
        bool any = false;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int tile = Edge(kind, width, height, x, y);
                if (tile >= 0)
                {
                    edges[(y * width) + x] = first + tile;
                    any = true;
                }
            }
        }

        if (any)
        {
            layers.Insert(back + 1, new MapLayer(EdgeLayer, edges));
        }
    }

    // The pair of a terrain over another. The sheet has no pair for deep water against the land, so such a cell takes the pair of the shallows:
    // the shore row of the deep water reads as a band of shallows.
    private static int PairFor(TerrainSheet.Ground upper, TerrainSheet.Ground lower)
    {
        int pair = TerrainSheet.PairOf(upper, lower);
        return pair < 0 && lower == TerrainSheet.Ground.Deep ? TerrainSheet.PairOf(upper, TerrainSheet.Ground.Shallow) : pair;
    }

    // The transition tile of a cell, or -1 when the cell keeps its fill.
    private static int Edge(int[] kind, int width, int height, int x, int y)
    {
        int own = kind[(y * width) + x];
        if (own < 0)
        {
            return -1;
        }

        TerrainSheet.Ground lower = (TerrainSheet.Ground)own;
        // For each corner (north-west, north-east, south-west, south-east) the terrain over the cell's own that wins it, or -1.
        Span<int> corner = stackalloc int[4];
        Span<int> votes = stackalloc int[s_rank.Length + 1];
        for (int c = 0; c < 4; c++)
        {
            int dx = (c & 1) == 0 ? -1 : 1;
            int dy = c < 2 ? -1 : 1;
            int best = -1;
            int bestRank = int.MaxValue;
            ReadOnlySpan<(int X, int Y)> around = [(x, y + dy), (x + dx, y), (x + dx, y + dy)];
            foreach ((int cx, int cy) in around)
            {
                if ((uint)cx >= (uint)width || (uint)cy >= (uint)height)
                {
                    continue;
                }

                int k = kind[(cy * width) + cx];
                if (k < 0 || k == own || PairFor((TerrainSheet.Ground)k, lower) < 0)
                {
                    continue;
                }

                int rank = Array.IndexOf(s_rank, (TerrainSheet.Ground)k);
                if (rank < bestRank)
                {
                    bestRank = rank;
                    best = k;
                }
            }

            corner[c] = best;
            if (best >= 0)
            {
                votes[Array.IndexOf(s_rank, (TerrainSheet.Ground)best)]++;
            }
        }

        // One upper terrain per tile: the one that has the most corners, the better ranked on a tie.
        int upper = -1;
        int most = 0;
        for (int r = 0; r < s_rank.Length; r++)
        {
            if (votes[r] > most)
            {
                most = votes[r];
                upper = (int)s_rank[r];
            }
        }

        if (upper < 0)
        {
            return -1;
        }

        int code = 0;
        for (int c = 0; c < 4; c++)
        {
            if (corner[c] == upper)
            {
                code |= 1 << c;
            }
        }

        if (code == 15)
        {
            // All four corners upper. A cell that is part of a run (a one-tile river or path) keeps its fill, or it would vanish into its
            // neighbours; a lone cell becomes a small patch, with one corner of its own terrain left.
            int same = 0;
            same += x > 0 && kind[(y * width) + x - 1] == own ? 1 : 0;
            same += x + 1 < width && kind[(y * width) + x + 1] == own ? 1 : 0;
            same += y > 0 && kind[((y - 1) * width) + x] == own ? 1 : 0;
            same += y + 1 < height && kind[((y + 1) * width) + x] == own ? 1 : 0;
            if (same > 1)
            {
                return -1;
            }

            code = 15 ^ (1 << ((int)(Noise.Of(x, y, 78) >> 8) % 4));
        }

        return code == 0 ? -1 : TerrainSheet.Transition(PairFor((TerrainSheet.Ground)upper, lower), code);
    }
}
