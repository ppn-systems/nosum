// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Maps;
using Nosum.Core;
using Nosum.Gameplay.Quests;
using Nosum.Rendering;

namespace Nosum.Wasm.Game;

/// <summary>
/// The active quest's waypoint on the playable map (docs/adr/ADR-090 D2): an arrow at the edge of the view while the place is off screen, and a dot on the
/// minimap. Both are drawn by the engine, with the frame's other records; the waypoint is only shown on the map it is on.
/// </summary>
internal sealed partial class MapScene
{
    private const int QuestArrowColour = unchecked((int)0xF5C542FFu);
    private const int QuestArrowShadow = unchecked((int)0x0B0D10B3u);
    private const int QuestDotColour = unchecked((int)0x4ADE80FFu);

    // A farmer this near an exit (feet to exit tile, world pixels) has the exit's direction label over the head; the name plate lifts by the label's band.
    private const int NameLiftReach = 40;
    private const int NameLiftPixels = 16;

    // Distance the arrow keeps from the view's edges, in world pixels (one u): clear of the HUD gutter and the top row.
    private const int QuestArrowInset = 24;

    private void DrawQuestWaypoint(MapContent content, int viewWidth, int viewHeight)
    {
        if (!this.TryGetWaypointHere(content.Spec.Id, out QuestWaypoint point))
        {
            return;
        }

        TileMap map = content.Map;
        double x = ((point.TileX + 0.5) * map.TileWidth) - _camera.Left;
        double y = ((point.TileY + 0.5) * map.TileHeight) - _camera.Top;
        if (EdgeArrow.TryPlace(x, y, viewWidth, viewHeight, QuestArrowInset, out EdgeArrowPlacement arrow))
        {
            EdgeArrow.Draw(_commands, arrow, QuestArrowColour, QuestArrowShadow);
        }
    }

    private void AddQuestDot(string mapId)
    {
        if (this.TryGetWaypointHere(mapId, out QuestWaypoint point))
        {
            _minimap.AddDot(point.TileX + 0.5, point.TileY + 0.5, QuestDotColour);
        }
    }

    private bool TryGetWaypointHere(string mapId, out QuestWaypoint point)
    {
        if (_waypoint?.Waypoint is { } found && string.Equals(found.MapId, mapId, StringComparison.Ordinal))
        {
            point = found;
            return true;
        }

        point = default;
        return false;
    }
}
