// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Rendering;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.Game;

/// <summary>
/// Pre-computed map exit / dungeon way-up marker drawn with zero allocations in the frame loop.
/// </summary>
/// <param name="MinTileX">Leftmost warp tile X.</param>
/// <param name="MaxTileX">Rightmost warp tile X.</param>
/// <param name="MinTileY">Topmost warp tile Y.</param>
/// <param name="MaxTileY">Bottommost warp tile Y.</param>
/// <param name="CenterWorldX">Center pixel X of the marker.</param>
/// <param name="CenterWorldY">Center pixel Y of the marker.</param>
/// <param name="LabelWorldX">Clamped world pixel X for the overhead destination label.</param>
/// <param name="LabelWorldY">Clamped world pixel Y for the overhead destination label.</param>
/// <param name="LabelId">Pre-interned <see cref="LabelRegistry"/> id, or 0 when none.</param>
/// <param name="IsMineWayUp">Whether this marker is a mine floor's way-up ladder / home exit.</param>
internal readonly record struct WayfindingMarker(
    int MinTileX,
    int MaxTileX,
    int MinTileY,
    int MaxTileY,
    int CenterWorldX,
    int CenterWorldY,
    int LabelWorldX,
    int LabelWorldY,
    int LabelId,
    bool IsMineWayUp);

/// <summary>
/// Draws map transition exit beacons, mine way-up ladders, and pre-interned destination labels so players always know
/// where to return home from the dungeon and where to walk to reach neighboring maps. Allocation-free in the frame loop.
/// </summary>
internal sealed class WayfindingDecorator : IMapDecorator
{
    private const int TileSize = 16;
    private readonly WayfindingMarker[] _markers;

    /// <summary>Initializes the wayfinding decorator.</summary>
    /// <param name="markers">Pre-computed markers for the map.</param>
    public WayfindingDecorator(WayfindingMarker[] markers)
    {
        ArgumentNullException.ThrowIfNull(markers);
        _markers = markers;
    }

    /// <inheritdoc/>
    public void DrawOnGround(RenderCommandBuffer commands, in Camera2D camera, long timeMs)
    {
        float pulse = MathF.Sin(timeMs * 0.005f);
        int bob = (int)(pulse * 2f);

        for (int i = 0; i < _markers.Length; i++)
        {
            WayfindingMarker m = _markers[i];
            int screenX = m.CenterWorldX - camera.Left;
            int screenY = m.CenterWorldY - camera.Top;

            if (m.IsMineWayUp)
            {
                int tileLeft = screenX - 8;
                int tileTop = screenY - 8;

                // Glowing way-up / return-home pad
                commands.FillRect(tileLeft + 1, tileTop + 1, 14, 14, unchecked((int)0x38bdf85f));
                commands.FillRect(tileLeft + 2, tileTop + 2, 12, 12, unchecked((int)0x0e7490af));

                // Wooden ladder rails & rungs leading up
                commands.FillRect(tileLeft + 4, tileTop + 2, 2, 12, unchecked((int)0xd97706f0));
                commands.FillRect(tileLeft + 10, tileTop + 2, 2, 12, unchecked((int)0xd97706f0));
                commands.FillRect(tileLeft + 4, tileTop + 4, 8, 2, unchecked((int)0xfbbf24f5));
                commands.FillRect(tileLeft + 4, tileTop + 8, 8, 2, unchecked((int)0xfbbf24f5));
                commands.FillRect(tileLeft + 4, tileTop + 12, 8, 2, unchecked((int)0xfbbf24f5));
            }
            else
            {
                // Subtle golden exit beacon chevron on map warp tiles
                commands.FillRect(screenX - 5, screenY - 5 + bob, 10, 10, unchecked((int)0xfacc154b));
                commands.FillRect(screenX - 3, screenY - 3 + bob, 6, 6, unchecked((int)0xfef08a9b));
            }
        }
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        for (int i = 0; i < _markers.Length; i++)
        {
            WayfindingMarker m = _markers[i];
            if (m.LabelId > 0)
            {
                world.Add(
                    RenderCommandBuffer.LabelTexture,
                    m.LabelId,
                    0,
                    0,
                    0,
                    m.LabelWorldX,
                    m.LabelWorldY,
                    m.CenterWorldY + TileSize,
                    m.CenterWorldX,
                    9200 + i);
            }
        }
    }
}

/// <summary>
/// Builds a <see cref="WayfindingDecorator"/> for each loaded map with pre-interned localized destination labels.
/// </summary>
internal sealed class WayfindingDecoratorProvider : IMapDecoratorProvider
{
    private readonly LabelRegistry _labels;
    private readonly IStringLocalizer<Language>? _text;

    /// <summary>Initializes the provider.</summary>
    /// <param name="labels">Shared label registry used to upload destination label bitmaps outside the frame loop.</param>
    /// <param name="text">Localized UI strings.</param>
    public WayfindingDecoratorProvider(LabelRegistry labels, IStringLocalizer<Language>? text)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _labels = labels;
        _text = text;
    }

    /// <inheritdoc/>
    public Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(map);

        List<MapWarp> warps = spec.ResolveWarps(map);
        List<WayfindingMarker> markers = [];
        int tileSize = Math.Max(1, map.TileWidth);
        int mapPixelWidth = map.Width * tileSize;
        int mapPixelHeight = map.Height * tileSize;

        if (spec.MineFloor >= 1)
        {
            int centerX = (spec.SpawnTileX * tileSize) + (tileSize / 2);
            int centerY = (spec.SpawnTileY * tileSize) + (tileSize / 2);
            int labelX = Math.Clamp(centerX, 44, Math.Max(44, mapPixelWidth - 44));
            int labelY = Math.Clamp((spec.SpawnTileY * tileSize) - 6, 8, Math.Max(8, mapPixelHeight - 20));
            string exitText = this.Localize("Wayfinding_MineExit", "↑ Lối lên · [E] Về nhà");
            int labelId = _labels.Intern(exitText, 1);
            markers.Add(new WayfindingMarker(
                spec.SpawnTileX,
                spec.SpawnTileX,
                spec.SpawnTileY,
                spec.SpawnTileY,
                centerX,
                centerY,
                labelX,
                labelY,
                labelId,
                IsMineWayUp: true));
            return Task.FromResult<IMapDecorator?>(new WayfindingDecorator([.. markers]));
        }

        if (string.Equals(spec.Id, GameMaps.Mine, StringComparison.Ordinal) && spec.Elevator is { } caveLift)
        {
            int liftCenterX = (caveLift.TileX * tileSize) + (tileSize / 2);
            int liftCenterY = (caveLift.TileY * tileSize) + (tileSize / 2);
            int liftLabelX = Math.Clamp(liftCenterX, 44, Math.Max(44, mapPixelWidth - 44));
            int liftLabelY = Math.Clamp((caveLift.TileY * tileSize) - 6, 8, Math.Max(8, mapPixelHeight - 20));
            string liftText = this.Localize("Wayfinding_MineElevator", "[E] Thang máy · Về nhà");
            markers.Add(new WayfindingMarker(
                caveLift.TileX,
                caveLift.TileX,
                caveLift.TileY,
                caveLift.TileY,
                liftCenterX,
                liftCenterY,
                liftLabelX,
                liftLabelY,
                _labels.Intern(liftText, 1),
                IsMineWayUp: false));
        }

        // Group walkable warp tiles that lead to the same destination map so contiguous exit strips share one centered beacon and label.
        WalkGrid grid = spec.BuildWalkGrid(map);
        Dictionary<string, (int MinX, int MaxX, int MinY, int MaxY)> grouped = new(StringComparer.Ordinal);
        for (int i = 0; i < warps.Count; i++)
        {
            MapWarp w = warps[i];
            if (string.Equals(spec.Id, GameMaps.BusStop, StringComparison.Ordinal)
                && string.Equals(w.TargetMap, "mine10", StringComparison.Ordinal))
            {
                // The BusStop dungeon rift portal is already rendered and labeled by MonstersDecorator.
                continue;
            }

            int tx = Math.Clamp(w.FromX, 0, Math.Max(0, map.Width - 1));
            int ty = Math.Clamp(w.FromY, 0, Math.Max(0, map.Height - 1));
            if (grid.IsBlocked(tx, ty))
            {
                continue;
            }

            if (grouped.TryGetValue(w.TargetMap, out (int MinX, int MaxX, int MinY, int MaxY) bounds))
            {
                grouped[w.TargetMap] = (
                    Math.Min(bounds.MinX, tx),
                    Math.Max(bounds.MaxX, tx),
                    Math.Min(bounds.MinY, ty),
                    Math.Max(bounds.MaxY, ty));
            }
            else
            {
                grouped[w.TargetMap] = (tx, tx, ty, ty);
            }
        }

        foreach (KeyValuePair<string, (int MinX, int MaxX, int MinY, int MaxY)> entry in grouped)
        {
            (int minX, int maxX, int minY, int maxY) = entry.Value;
            int centerTileX = (minX + maxX) / 2;
            int centerTileY = (minY + maxY) / 2;
            int centerX = (centerTileX * tileSize) + (tileSize / 2);
            int centerY = (centerTileY * tileSize) + (tileSize / 2);

            string arrow = PickArrow(centerTileX, centerTileY, map.Width, map.Height);
            string targetName = this.TargetDisplayName(entry.Key);
            int labelId = _labels.Intern(arrow + " " + targetName, 1);
            int labelX = Math.Clamp(centerX, 40, Math.Max(40, mapPixelWidth - 40));
            int labelY = Math.Clamp((centerTileY * tileSize) - 5, 8, Math.Max(8, mapPixelHeight - 20));

            markers.Add(new WayfindingMarker(
                minX,
                maxX,
                minY,
                maxY,
                centerX,
                centerY,
                labelX,
                labelY,
                labelId,
                IsMineWayUp: false));
        }

        return Task.FromResult<IMapDecorator?>(markers.Count > 0 ? new WayfindingDecorator([.. markers]) : null);
    }

    private static string PickArrow(int tileX, int tileY, int width, int height)
    {
        int distLeft = tileX;
        int distRight = Math.Max(0, width - 1 - tileX);
        int distTop = tileY;
        int distBottom = Math.Max(0, height - 1 - tileY);
        int minH = Math.Min(distLeft, distRight);
        int minV = Math.Min(distTop, distBottom);

        if (minV <= minH && minV <= 3)
        {
            return distBottom <= distTop ? "↓" : "↑";
        }

        if (minH <= 3)
        {
            return distLeft <= distRight ? "←" : "→";
        }

        return distBottom <= distTop ? "↓" : "↑";
    }

    private string TargetDisplayName(string targetMap)
    {
        if (string.Equals(targetMap, "mine1", StringComparison.Ordinal))
        {
            return this.Localize("MapName_mine1", "Tầng hầm 1");
        }

        if (string.Equals(targetMap, GameMaps.CoopFarm, StringComparison.Ordinal))
        {
            return this.Localize("MapName_coopfarm", this.Localize("MapName_coop_farm", "Đất hợp tác xã"));
        }

        return this.Localize("MapName_" + targetMap, targetMap);
    }

    private string Localize(string key, string fallback)
    {
        if (_text is null)
        {
            return fallback;
        }

        LocalizedString found = _text[key];
        return found.ResourceNotFound ? fallback : found.Value;
    }
}
