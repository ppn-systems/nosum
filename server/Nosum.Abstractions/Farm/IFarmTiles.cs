// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.Farm;

/// <summary>The worked tiles of an instance, for layers that must keep off them (docs/adr/ADR-024 D3: nothing is placed on tilled soil).</summary>
public interface IFarmTiles
{
    /// <summary>Returns which tiles of a rectangle are tilled.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="tileX">Left tile of the rectangle.</param>
    /// <param name="tileY">Top tile of the rectangle.</param>
    /// <param name="width">Width in tiles.</param>
    /// <param name="height">Height in tiles.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The tilled tiles inside the rectangle; empty on a map that cannot be farmed.</returns>
    Task<IReadOnlySet<(int X, int Y)>> WorkedTilesAsync(MapInstance instance, int tileX, int tileY, int width, int height, CancellationToken cancellationToken = default);
}
