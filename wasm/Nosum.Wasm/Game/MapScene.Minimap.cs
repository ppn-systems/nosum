// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Core;
using Nosum.Core.Interpolation;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Npcs;
using Nosum.Rendering;

namespace Nosum.Wasm.Game;

/// <summary>
/// The minimap of the playable world (the M key): the map downscaled into the frame in the top-right corner of the HUD, with dots for the farmer,
/// the other players, the NPCs and the exits. The frame is HUD markup (HudMinimap.razor); this draws the map inside it. Hidden by default on a narrow
/// screen, where the world map button stands in for it; the M key shows or hides it.
/// </summary>
internal sealed partial class MapScene
{
    private const int MinimapBorderColour = unchecked((int)0x0B0D10FFu);
    private const int MinimapFrameColour = unchecked((int)0x14171CCCu);
    private const int FarmerDotColour = unchecked((int)0xFFF27DFFu);
    private const int RemoteDotColour = unchecked((int)0x7FD7FFFFu);
    private const int NpcDotColour = unchecked((int)0xFF9F43FFu);
    private const int ExitDotColour = unchecked((int)0xC084FCFFu);

    // The HUD frame of the minimap, in UI pixels (one u is one world pixel, the scale of the map). It sits in the top-right corner: the screen
    // gutter, then the calendar bar, then a gap; the frame runs left of the system column, which is two 16 u buttons and a gap wide, with the same
    // gap between them. HudMinimap.razor.css places the frame with these numbers; change both together.
    private const int HudEdge = 4;
    private const int HudBarHeight = 18;
    private const int HudGap = 2;
    private const int HudColumnWidth = 34;
    private const int HudFrameWidth = 60;
    private const int HudFrameEdge = 5;

    private readonly MinimapLayer _minimap = new();

    // The player's choice with M this session; null until the first press, when the screen's width decides.
    private bool? _minimapChoice;

    private void DrawMinimap(MapContent content, InputEdges edges, int canvasWidth, int canvasHeight, int scale)
    {
        bool narrow = canvasWidth < NarrowCanvasWidth;
        if ((edges & InputEdges.Minimap) != 0)
        {
            _minimapChoice = !(_minimapChoice ?? !narrow);
        }

        bool shown = _minimapChoice ?? !narrow;
        if (_minimapFrame is not null)
        {
            _minimapFrame.Shown = shown;
        }

        // A panel over the corner hides the map too (MinimapViewModel.Covered).
        _minimap.Visible = shown && _minimapFrame?.Covered != true;
        if (!_minimap.Visible)
        {
            return;
        }

        string id = content.Spec.Id;
        if (_minimap.Key != id && _minimap.Build(content.Map, id, MinimapPalette.Default))
        {
            // The NPCs and the exits do not move: they are kept with the ground and drawn with it.
            IReadOnlyList<NpcDefinition> npcs = NpcCatalog.OnMap(id);
            for (int i = 0; i < npcs.Count; i++)
            {
                _minimap.AddMarker(npcs[i].TileX + 0.5, npcs[i].TileY + 0.5, NpcDotColour);
            }

            for (int i = 0; i < content.Warps.Count; i++)
            {
                _minimap.AddMarker(content.Warps[i].FromX + 0.5, content.Warps[i].FromY + 0.5, ExitDotColour);
            }
        }

        TileMap map = content.Map;
        _minimap.BeginDots();
        _minimap.AddDot(_farmer.X / map.TileWidth, _farmer.Y / map.TileHeight, FarmerDotColour);
        if (_remote is not null && _phase != Phase.FadingOut)
        {
            double now = RemotePlayers.LocalNow();
            for (int i = 0; i < _remote.Count; i++)
            {
                if (_remote.TryGetPose(_remote.IdAt(i), now, out EntityPose pose))
                {
                    _minimap.AddDot(pose.X / map.TileWidth, pose.Y / map.TileHeight, RemoteDotColour);
                }
            }
        }

        // The frame's inside (MinimapLayer.MaxWidth by MaxHeight) is the dark backing; the map is centred on it.
        int worldWidth = (canvasWidth + scale - 1) / scale;
        int frameRight = worldWidth - HudEdge - HudColumnWidth - HudGap;
        int contentLeft = frameRight - HudFrameWidth + HudFrameEdge;
        int contentTop = HudEdge + HudBarHeight + HudGap + HudFrameEdge;
        _commands.FillRect(contentLeft, contentTop, MinimapLayer.MaxWidth, MinimapLayer.MaxHeight, MinimapFrameColour);
        int left = contentLeft + ((MinimapLayer.MaxWidth - _minimap.BoxWidth) / 2);
        int top = contentTop + ((MinimapLayer.MaxHeight - _minimap.BoxHeight) / 2);
        _minimap.Draw(_commands, left, top, MinimapFrameColour, MinimapBorderColour);
    }
}
