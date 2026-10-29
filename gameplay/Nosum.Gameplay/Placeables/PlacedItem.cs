// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Placeables;

/// <summary>One placeable standing on the ground of an instance.</summary>
/// <param name="Id">Id unique across the game (the database key); never reused.</param>
/// <param name="ItemId">The inventory item it is made from, which names its <see cref="PlaceableDefinition"/>.</param>
/// <param name="TileX">Left tile of its footprint.</param>
/// <param name="TileY">Top tile of its footprint.</param>
/// <param name="Rotation">Rotation, zero to the definition's rotations minus one.</param>
/// <param name="PlacedAtMs">When it was placed, Unix milliseconds; a sprinkler waters the game days after the day it was placed (docs/adr/ADR-024 D4).</param>
public readonly record struct PlacedItem(int Id, int ItemId, int TileX, int TileY, int Rotation, long PlacedAtMs = 0);

/// <summary>Why a placement is allowed or not. The server maps it to a wire status.</summary>
public enum PlacementVerdict
{
    /// <summary>The footprint is free.</summary>
    Ok = 0,

    /// <summary>The rotation does not exist for this piece.</summary>
    BadRotation = 1,

    /// <summary>Part of the footprint is outside the map.</summary>
    OutsideRoom = 2,

    /// <summary>Part of the footprint is a wall, a building or other fixed obstacle.</summary>
    Blocked = 3,

    /// <summary>Part of the footprint is a tile the room keeps clear (the door and where visitors arrive).</summary>
    Reserved = 4,

    /// <summary>Another piece is already there.</summary>
    Overlaps = 5,

    /// <summary>Part of the footprint is tilled soil (docs/adr/ADR-024 D3).</summary>
    Tilled = 6,

    /// <summary>Part of the footprint holds a resource node: a tree, a stone, a weed (docs/adr/ADR-024 D3).</summary>
    Node = 7,
}
