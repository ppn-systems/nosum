// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Core;

namespace Nosum.Rendering;

/// <summary>
/// Which tile layers of a map are drawn below the characters (ground) and which over them (front), by the layer-name
/// convention of the early maps (docs/adr/ADR-007): names starting with <c>Front</c> or <c>AlwaysFront</c> draw over
/// the characters, <c>Paths</c> is a logic layer that is not drawn, everything else (<c>Back</c>, <c>Back2</c>,
/// <c>Buildings</c>, <c>Buildings2</c>, ...) is ground. Order in the map file is kept.
/// </summary>
public sealed class TileLayerPlan
{
    private static readonly string[] s_frontPrefixes = ["Front", "AlwaysFront"];

    private readonly TileLayer[] _ground;
    private readonly TileLayer[] _front;

    private TileLayerPlan(TileLayer[] ground, TileLayer[] front)
    {
        _ground = ground;
        _front = front;
    }

    /// <summary>Gets the layers drawn below the characters, in order. A span, so the frame can loop over it without allocating.</summary>
    public ReadOnlySpan<TileLayer> Ground => _ground;

    /// <summary>Gets the layers drawn over the characters, in order.</summary>
    public ReadOnlySpan<TileLayer> Front => _front;

    /// <summary>Sorts a map's layers into ground and front.</summary>
    /// <param name="map">The map.</param>
    /// <returns>The plan.</returns>
    public static TileLayerPlan For(TileMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        List<TileLayer> ground = [];
        List<TileLayer> front = [];
        foreach (TileLayer layer in map.Layers)
        {
            if (string.Equals(layer.Name, "Paths", StringComparison.Ordinal))
            {
                continue;
            }

            (IsFront(layer.Name) ? front : ground).Add(layer);
        }

        return new TileLayerPlan([.. ground], [.. front]);
    }

    private static bool IsFront(string name)
    {
        foreach (string prefix in s_frontPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
