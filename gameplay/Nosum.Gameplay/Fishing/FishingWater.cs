// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Maps;

namespace Nosum.Gameplay.Fishing;

/// <summary>
/// The places a line can be cast into (docs/adr/ADR-078 D2). Each flag is a map whose water tiles the rod reaches: the Beach sea, the
/// Woods pond and the farm pond. The numbers are wire values of the fishing packets; append-only.
/// </summary>
[Flags]
public enum FishingWater : byte
{
    /// <summary>No fishing here.</summary>
    None = 0,

    /// <summary>The sea on the Beach.</summary>
    Beach = 1,

    /// <summary>The pond in the Woods.</summary>
    Woods = 2,

    /// <summary>The pond on the player's farm.</summary>
    Farm = 4,
}

/// <summary>Maps a map id to the water it has for fishing.</summary>
public static class FishingWaters
{
    /// <summary>Gets the fishing water of a map.</summary>
    /// <param name="mapId">The map id (<see cref="GameMaps"/>).</param>
    /// <returns>The water flag; <see cref="FishingWater.None"/> for a map without fishing.</returns>
    public static FishingWater Of(string mapId) => mapId switch
    {
        GameMaps.Beach => FishingWater.Beach,
        GameMaps.Woods => FishingWater.Woods,
        GameMaps.Farm => FishingWater.Farm,
        _ => FishingWater.None,
    };
}
