// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Nosum.Abstractions.Placeables;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;

namespace Nosum.Infrastructure.Placeables;

/// <summary>
/// <see cref="IPlacedLookup"/>: the pieces of every loaded instance as immutable snapshots, replaced by <see cref="PlaceableService"/> whenever an instance changes and read
/// without a lock by the farm layer (a tile under a piece cannot be tilled) and the growth rule (sprinklers). It is a separate singleton so the farm and the placeables do not
/// need each other to be built (docs/adr/ADR-024 D3, D4).
/// </summary>
public sealed class PlacedTiles : IPlacedLookup
{
    private static readonly HashSet<(int X, int Y)> s_none = [];

    private readonly ConcurrentDictionary<string, Snapshot> _instances = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyList<PlacedItem> ItemsOf(MapInstance instance) => _instances.TryGetValue(instance.Key, out Snapshot? snapshot) ? snapshot.Items : [];

    /// <inheritdoc/>
    public IReadOnlySet<(int X, int Y)> CoveredTiles(MapInstance instance) => _instances.TryGetValue(instance.Key, out Snapshot? snapshot) ? snapshot.Covered : s_none;

    /// <inheritdoc/>
    public bool IsCovered(MapInstance instance, int tileX, int tileY) => _instances.TryGetValue(instance.Key, out Snapshot? snapshot) && snapshot.Covered.Contains((tileX, tileY));

    /// <inheritdoc/>
    public bool IsSprinklerWatered(MapInstance instance, int tileX, int tileY) => _instances.TryGetValue(instance.Key, out Snapshot? snapshot) && SprinklerRules.IsWatered(snapshot.Items, tileX, tileY);

    /// <summary>Replaces what is known about an instance.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="items">The pieces now standing there.</param>
    internal void Publish(MapInstance instance, IReadOnlyList<PlacedItem> items)
    {
        HashSet<(int X, int Y)> covered = [];
        PlacedItem[] copy = [.. items];
        foreach (PlacedItem item in copy)
        {
            if (!PlaceableCatalog.TryGet(item.ItemId, out PlaceableDefinition? definition))
            {
                continue;
            }

            PlaceableView view = definition.View(Math.Min(item.Rotation, definition.Rotations - 1));
            for (int y = item.TileY; y < item.TileY + view.FootprintHeight; y++)
            {
                for (int x = item.TileX; x < item.TileX + view.FootprintWidth; x++)
                {
                    _ = covered.Add((x, y));
                }
            }
        }

        _instances[instance.Key] = new Snapshot(copy, covered);
    }

    private sealed record Snapshot(PlacedItem[] Items, HashSet<(int X, int Y)> Covered);
}
