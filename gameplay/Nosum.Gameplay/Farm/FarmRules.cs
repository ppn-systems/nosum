// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Farm;

/// <summary>
/// The pure farm rules: tilling, planting, watering, harvesting and crop growth. Time is a
/// parameter (Unix milliseconds), so the server and the client compute the same stage from the same
/// stored timestamps and tests need no clock. A crop grows only while its soil is wet: watering wets
/// the soil for <see cref="WetDurationMs"/>, and growth is the wet time that has passed since planting.
/// </summary>
public static class FarmRules
{
    /// <summary>How long watering keeps the soil wet, milliseconds.</summary>
    public const long WetDurationMs = 90_000;

    /// <summary>Farthest distance from the player's feet to the centre of the target tile, world pixels.</summary>
    public const double MaxReachPixels = 32;

    /// <summary>Returns whether a tile centre is within reach of the player's feet.</summary>
    /// <param name="feetX">Feet x, world pixels.</param>
    /// <param name="feetY">Feet y, world pixels.</param>
    /// <param name="tileX">Target tile x.</param>
    /// <param name="tileY">Target tile y.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <returns><see langword="true"/> when the tile can be worked from there.</returns>
    public static bool InReach(double feetX, double feetY, int tileX, int tileY, int tileSize)
    {
        double dx = ((tileX + 0.5) * tileSize) - feetX;
        double dy = ((tileY + 0.5) * tileSize) - feetY;
        return (dx * dx) + (dy * dy) <= MaxReachPixels * MaxReachPixels;
    }

    /// <summary>Tills a tile.</summary>
    /// <param name="tillable">Whether the map allows tilling there.</param>
    /// <param name="current">The tile now, or <see langword="null"/> when untilled.</param>
    /// <param name="next">The tilled tile when allowed.</param>
    /// <returns>The verdict.</returns>
    public static FarmVerdict Till(bool tillable, FarmTile? current, out FarmTile next)
    {
        next = FarmTile.Bare;
        if (current is not null)
        {
            return FarmVerdict.AlreadyTilled;
        }

        return tillable ? FarmVerdict.Ok : FarmVerdict.NotTillable;
    }

    /// <summary>Plants a seed's crop on tilled, empty soil.</summary>
    /// <param name="current">The tile now, or <see langword="null"/> when untilled.</param>
    /// <param name="cropId">The crop to plant.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="next">The planted tile when allowed.</param>
    /// <returns>The verdict.</returns>
    public static FarmVerdict Plant(FarmTile? current, int cropId, long nowMs, out FarmTile next)
    {
        next = default;
        if (current is not { } tile)
        {
            return FarmVerdict.NotTilled;
        }

        if (tile.HasCrop)
        {
            return FarmVerdict.HasCrop;
        }

        if (!CropCatalog.TryGet(cropId, out _))
        {
            return FarmVerdict.UnknownCrop;
        }

        next = new FarmTile(cropId, nowMs, 0, nowMs, tile.WetUntilMs);
        return FarmVerdict.Ok;
    }

    /// <summary>Waters tilled soil: settles the growth so far, then wets the soil from now.</summary>
    /// <param name="current">The tile now, or <see langword="null"/> when untilled.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="next">The watered tile when allowed.</param>
    /// <returns>The verdict.</returns>
    public static FarmVerdict Water(FarmTile? current, long nowMs, out FarmTile next)
    {
        next = default;
        if (current is not { } tile)
        {
            return FarmVerdict.NotTilled;
        }

        long settled = tile.HasCrop ? GrowthAt(tile, nowMs) : 0;
        next = tile with { GrowthMs = settled, SettledAtMs = nowMs, WetUntilMs = nowMs + WetDurationMs };
        return FarmVerdict.Ok;
    }

    /// <summary>Harvests a mature crop; the soil stays tilled.</summary>
    /// <param name="current">The tile now, or <see langword="null"/> when untilled.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="next">The bare tile when allowed.</param>
    /// <param name="crop">The crop that was harvested when allowed.</param>
    /// <returns>The verdict.</returns>
    public static FarmVerdict Harvest(FarmTile? current, long nowMs, out FarmTile next, out CropDefinition? crop)
    {
        next = default;
        crop = null;
        if (current is not { } tile)
        {
            return FarmVerdict.NotTilled;
        }

        if (!tile.HasCrop || !CropCatalog.TryGet(tile.CropId, out CropDefinition? found))
        {
            return FarmVerdict.NothingToDo;
        }

        if (!IsMature(tile, nowMs))
        {
            return FarmVerdict.NotMature;
        }

        crop = found;
        next = new FarmTile(0, 0, 0, nowMs, tile.WetUntilMs);
        return FarmVerdict.Ok;
    }

    /// <summary>Returns the watered growth time a crop has reached, capped at what it needs to mature.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <returns>Milliseconds of watered growth; zero for bare soil.</returns>
    public static long GrowthAt(in FarmTile tile, long nowMs)
    {
        if (!tile.HasCrop || !CropCatalog.TryGet(tile.CropId, out CropDefinition? crop))
        {
            return 0;
        }

        // Wet time since the last settle: the soil was wet from the last watering (which settled) until WetUntil.
        long wetEnd = Math.Min(nowMs, tile.WetUntilMs);
        long wetMs = Math.Max(0, wetEnd - tile.SettledAtMs);
        return Math.Min(crop.TotalGrowthMs, tile.GrowthMs + wetMs);
    }

    /// <summary>Returns the stage a tile's crop shows, from 0 (just planted) to mature.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <returns>The stage, or -1 for bare soil.</returns>
    public static int StageAt(in FarmTile tile, long nowMs)
        => tile.HasCrop && CropCatalog.TryGet(tile.CropId, out CropDefinition? crop) ? crop.StageAt(GrowthAt(tile, nowMs)) : -1;

    /// <summary>Returns whether a tile's crop can be harvested.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <returns><see langword="true"/> when a mature crop grows there.</returns>
    public static bool IsMature(in FarmTile tile, long nowMs)
        => tile.HasCrop && CropCatalog.TryGet(tile.CropId, out CropDefinition? crop) && GrowthAt(tile, nowMs) >= crop.TotalGrowthMs;

    /// <summary>Returns whether the soil is wet at a moment.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <returns><see langword="true"/> while the watering lasts.</returns>
    public static bool IsWet(in FarmTile tile, long nowMs) => nowMs < tile.WetUntilMs;
}
