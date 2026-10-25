// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Abstractions.Presence;

/// <summary>Names the Nalix connection group that holds the players of one map instance.</summary>
public static class MapGroup
{
    /// <summary>Returns the group name for an instance key (the map id for a shared map). Any feature can multicast to a place through this group.</summary>
    /// <param name="instanceKey">Instance key, <see cref="Nosum.Gameplay.Maps.MapInstance.Key"/>.</param>
    /// <returns>The Nalix group name.</returns>
    public static string Name(string instanceKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceKey);
        return "map:" + instanceKey;
    }
}
