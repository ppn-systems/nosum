// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Movement;

namespace Nosum.Gameplay.Nodes;

/// <summary>
/// A request for node sites a map file does not mark: <see cref="Count"/> tiles of one kind spread over a rectangle (docs/adr/ADR-037 D2). The sites are the spots
/// <see cref="NodePlacements"/> thins and regrows from, exactly like the glyphs of the <c>Paths</c> layer.
/// </summary>
/// <param name="Kind">The kind of node the sites hold.</param>
/// <param name="Count">How many sites to find.</param>
/// <param name="X0">Left tile of the rectangle.</param>
/// <param name="Y0">Top tile.</param>
/// <param name="X1">Right tile (included).</param>
/// <param name="Y1">Bottom tile (included).</param>
public readonly record struct NodeScatter(NodeKind Kind, int Count, int X0, int Y0, int X1, int Y1)
{
    /// <summary>
    /// Picks the sites: random open tiles of the rectangle whose four neighbours are open too, so no site sits in a one-tile corridor where a stone could wall a
    /// path off. The same seed always gives the same sites.
    /// </summary>
    /// <param name="grid">The map's walk grid.</param>
    /// <param name="seed">Deterministic seed; one per map and scatter.</param>
    /// <returns>Up to <see cref="Count"/> sites, fewer when the rectangle has no room.</returns>
    public List<NodeSite> Sites(WalkGrid grid, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(grid);

        List<NodeSite> sites = [];
        int width = Math.Max(1, this.X1 - this.X0 + 1);
        int height = Math.Max(1, this.Y1 - this.Y0 + 1);
        NodeRandom random = new(seed);
        HashSet<(int, int)> taken = [];
        int attempts = this.Count * 40;
        while (attempts-- > 0 && sites.Count < this.Count)
        {
            int x = this.X0 + random.Next(width);
            int y = this.Y0 + random.Next(height);
            if (IsRoomy(grid, x, y) && taken.Add((x, y)))
            {
                sites.Add(new NodeSite(x, y, this.Kind, 0));
            }
        }

        return sites;
    }

    private static bool IsRoomy(WalkGrid grid, int x, int y)
        => !grid.IsBlocked(x, y) && !grid.IsBlocked(x - 1, y) && !grid.IsBlocked(x + 1, y) && !grid.IsBlocked(x, y - 1) && !grid.IsBlocked(x, y + 1);
}
