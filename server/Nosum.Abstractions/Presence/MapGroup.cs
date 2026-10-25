// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Abstractions.Presence;

/// <summary>Names the Nalix connection group that holds the players of one map.</summary>
public static class MapGroup
{
    /// <summary>Returns the group name for a map. Any feature can multicast to a map through this group.</summary>
    /// <param name="mapId">Map key.</param>
    /// <returns>The Nalix group name.</returns>
    public static string Name(string mapId)
    {
        ArgumentException.ThrowIfNullOrEmpty(mapId);
        return "map:" + mapId;
    }
}
