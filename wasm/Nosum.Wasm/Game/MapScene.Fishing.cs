// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Fishing;
using Nosum.Gameplay.Maps;

namespace Nosum.Wasm.Game;

/// <summary>The fishing rod's click on the map (docs/adr/ADR-071 D1, D9).</summary>
internal sealed partial class MapScene
{
    // The water of the map on screen. Built on the first click with a rod, and again when the map changes; the walk grid is not enough, because
    // it blocks water for walking and the rod needs the tiles that are water.
    private WaterGrid? _water;
    private MapContent? _waterOf;

    // A click on water while a fishing rod is held casts into it. Only a fishing map has water to cast into. The tile is only a hint for the
    // server, which checks the water, the reach, the rod and the energy itself.
    private void CastAtPointer(MapSpec spec, int tileSize, int scale)
    {
        if (_actions is null || !_actions.HoldsRod || FishingWaters.Of(spec.Id) == FishingWater.None)
        {
            return;
        }

        if (!ReferenceEquals(_waterOf, _content))
        {
            _water = WaterGrid.FromMap(_content.Map);
            _waterOf = _content;
        }

        double worldX = _camera.Left + ((double)_pointerX / scale);
        double worldY = _camera.Top + ((double)_pointerY / scale);
        int tileX = (int)Math.Floor(worldX / tileSize);
        int tileY = (int)Math.Floor(worldY / tileSize);
        if (_water?.IsWater(tileX, tileY) == true)
        {
            _actions.RequestCast(tileX, tileY);
        }
    }
}
