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
/// The minimap of the playable world (the M key): the map downscaled into the top-right corner of the same frame, with dots for the farmer,
/// the other players, the NPCs and the exits. Hidden by default on a narrow screen (a touch device); the M key shows or hides it.
/// </summary>
internal sealed partial class MapScene
{
    private const int MinimapBorderColour = unchecked((int)0x0B0D10FFu);
    private const int MinimapFrameColour = unchecked((int)0x14171CCCu);
    private const int FarmerDotColour = unchecked((int)0xFFF27DFFu);
    private const int RemoteDotColour = unchecked((int)0x7FD7FFFFu);
    private const int NpcDotColour = unchecked((int)0xFF9F43FFu);
    private const int ExitDotColour = unchecked((int)0xC084FCFFu);

    // From this canvas width the HUD's system column sits at the right edge; its width (about 96 CSS pixels with the buttons) is measured by eye, not by layout.
    private const int WideCanvasWidth = 1600;
    private const int HudRightColumnCssPixels = 96;

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

        _minimap.Visible = _minimapChoice ?? !narrow;
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

        // On a wide window the HUD's system buttons wrap into a column at the right edge (GameHud); the box keeps clear of it.
        int rightInset = canvasWidth >= WideCanvasWidth ? HudRightColumnCssPixels / scale : 0;
        _minimap.Draw(_commands, (canvasWidth + scale - 1) / scale, (canvasHeight + scale - 1) / scale, rightInset, HudBottomInset(canvasWidth, canvasHeight) / scale, MinimapFrameColour, MinimapBorderColour);
    }
}
