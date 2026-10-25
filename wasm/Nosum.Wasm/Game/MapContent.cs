// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Rendering;

namespace Nosum.Wasm.Game;

/// <summary>Everything the scene needs to show and walk one map, loaded and ready: nothing in it is fetched during a frame.</summary>
/// <param name="Spec">The catalogue entry.</param>
/// <param name="Map">The parsed Tiled map.</param>
/// <param name="TileSetTextures">Texture id per tileset of <paramref name="Map"/>.</param>
/// <param name="Layers">Which layers draw below and over the characters.</param>
/// <param name="Grid">The walk grid, built by the same rule as the server's.</param>
/// <param name="Warps">The map's exits (only to show where they lead and to prefetch the next map; the server decides when one is taken).</param>
/// <param name="Decorators">What the map draws besides its layers.</param>
internal sealed record MapContent(
    MapSpec Spec,
    TileMap Map,
    int[] TileSetTextures,
    TileLayerPlan Layers,
    WalkGrid Grid,
    IReadOnlyList<MapWarp> Warps,
    IMapDecorator[] Decorators);
