// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Movement;

namespace Nosum.Gameplay.Placeables;

/// <summary>
/// What is placed in one instance, with the rules for adding to it: the footprint must be inside the map, on free
/// floor (not blocked by the map), not on a reserved tile and not on another piece. Pure and shared: the server decides
/// with it, the client builds the same collision grid from the same set to predict walking
/// (docs/adr/ADR-015). Not thread-safe; the owner serializes access.
/// </summary>
public sealed class PlacedSet
{
    private readonly List<PlacedItem> _items = [];

    /// <summary>Gets the pieces, in the order they were added.</summary>
    public IReadOnlyList<PlacedItem> Items => _items;

    /// <summary>Gets the number of pieces.</summary>
    public int Count => _items.Count;

    /// <summary>Finds a piece by id.</summary>
    /// <param name="id">Piece id.</param>
    /// <param name="item">The piece when found.</param>
    /// <returns><see langword="true"/> when it exists.</returns>
    public bool TryGet(int id, out PlacedItem item)
    {
        foreach (PlacedItem candidate in _items)
        {
            if (candidate.Id == id)
            {
                item = candidate;
                return true;
            }
        }

        item = default;
        return false;
    }

    /// <summary>Finds the piece whose footprint covers a tile.</summary>
    /// <param name="tileX">Tile x.</param>
    /// <param name="tileY">Tile y.</param>
    /// <param name="item">The piece when one covers the tile.</param>
    /// <returns><see langword="true"/> when a piece stands on the tile.</returns>
    public bool TryFindAt(int tileX, int tileY, out PlacedItem item)
    {
        foreach (PlacedItem candidate in _items)
        {
            if (PlaceableCatalog.TryGet(candidate.ItemId, out PlaceableDefinition? definition))
            {
                PlaceableView view = definition.View(Math.Min(candidate.Rotation, definition.Rotations - 1));
                if (tileX >= candidate.TileX && tileX < candidate.TileX + view.FootprintWidth && tileY >= candidate.TileY && tileY < candidate.TileY + view.FootprintHeight)
                {
                    item = candidate;
                    return true;
                }
            }
        }

        item = default;
        return false;
    }

    /// <summary>Adds a piece without checking it; call <see cref="Check"/> first.</summary>
    /// <param name="item">The piece.</param>
    public void Add(PlacedItem item) => _items.Add(item);

    /// <summary>Replaces the piece with the same id.</summary>
    /// <param name="item">The new state of an existing piece.</param>
    /// <returns><see langword="true"/> when the piece existed.</returns>
    public bool Replace(PlacedItem item)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Id == item.Id)
            {
                _items[i] = item;
                return true;
            }
        }

        return false;
    }

    /// <summary>Removes a piece.</summary>
    /// <param name="id">Piece id.</param>
    /// <returns><see langword="true"/> when it existed.</returns>
    public bool Remove(int id)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Id == id)
            {
                _items.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>Empties the set.</summary>
    public void Clear() => _items.Clear();

    /// <summary>Checks whether a piece may stand somewhere.</summary>
    /// <param name="definition">What is placed.</param>
    /// <param name="rotation">Its rotation.</param>
    /// <param name="tileX">Left tile of the footprint.</param>
    /// <param name="tileY">Top tile of the footprint.</param>
    /// <param name="mapGrid">The map's own grid, without any placed pieces.</param>
    /// <param name="reserved">Tiles the room keeps clear.</param>
    /// <param name="ignoreId">A piece to treat as absent (the one being moved), or zero.</param>
    /// <returns>Ok, or the first reason it cannot stand there.</returns>
    public PlacementVerdict Check(PlaceableDefinition definition, int rotation, int tileX, int tileY, WalkGrid mapGrid, IReadOnlySet<(int X, int Y)> reserved, int ignoreId = 0)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(mapGrid);
        ArgumentNullException.ThrowIfNull(reserved);

        if ((uint)rotation >= (uint)definition.Rotations)
        {
            return PlacementVerdict.BadRotation;
        }

        PlaceableView view = definition.View(rotation);
        for (int y = tileY; y < tileY + view.FootprintHeight; y++)
        {
            for (int x = tileX; x < tileX + view.FootprintWidth; x++)
            {
                if (x < 0 || y < 0 || x >= mapGrid.Width || y >= mapGrid.Height)
                {
                    return PlacementVerdict.OutsideRoom;
                }

                if (mapGrid.IsBlocked(x, y))
                {
                    return PlacementVerdict.Blocked;
                }

                if (reserved.Contains((x, y)))
                {
                    return PlacementVerdict.Reserved;
                }
            }
        }

        foreach (PlacedItem other in _items)
        {
            if (other.Id == ignoreId || !PlaceableCatalog.TryGet(other.ItemId, out PlaceableDefinition? otherDefinition))
            {
                continue;
            }

            PlaceableView otherView = otherDefinition.View(Math.Min(other.Rotation, otherDefinition.Rotations - 1));
            bool apart = other.TileX + otherView.FootprintWidth <= tileX
                || tileX + view.FootprintWidth <= other.TileX
                || other.TileY + otherView.FootprintHeight <= tileY
                || tileY + view.FootprintHeight <= other.TileY;
            if (!apart)
            {
                return PlacementVerdict.Overlaps;
            }
        }

        return PlacementVerdict.Ok;
    }

    /// <summary>Returns a copy of the map's grid with every piece's footprint blocked: the grid players walk on in this instance.</summary>
    /// <param name="mapGrid">The map's own grid; it is not changed.</param>
    /// <returns>The copy.</returns>
    public WalkGrid BuildGrid(WalkGrid mapGrid)
    {
        ArgumentNullException.ThrowIfNull(mapGrid);

        WalkGrid grid = mapGrid.Clone();
        foreach (PlacedItem item in _items)
        {
            if (PlaceableCatalog.TryGet(item.ItemId, out PlaceableDefinition? definition))
            {
                PlaceableView view = definition.View(Math.Min(item.Rotation, definition.Rotations - 1));
                grid.Block(item.TileX, item.TileY, view.FootprintWidth, view.FootprintHeight);
            }
        }

        return grid;
    }
}
