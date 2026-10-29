// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.Nodes;

/// <summary>The nodes of an instance, for layers that must keep off them (docs/adr/ADR-024 D3: nothing is placed on a tree, a stone or a weed).</summary>
public interface INodeTiles
{
    /// <summary>Returns which tiles of a rectangle hold a resource node of any kind.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="tileX">Left tile of the rectangle.</param>
    /// <param name="tileY">Top tile of the rectangle.</param>
    /// <param name="width">Width in tiles.</param>
    /// <param name="height">Height in tiles.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The tiles inside the rectangle that hold a node; empty on a map without nodes.</returns>
    Task<IReadOnlySet<(int X, int Y)>> NodeTilesAsync(MapInstance instance, int tileX, int tileY, int width, int height, CancellationToken cancellationToken = default);
}
