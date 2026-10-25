// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Placeable;

/// <summary>Outcome code of a placeable request. Clients map it to text. Values are append-only.</summary>
public enum PlaceStatus : byte
{
    /// <summary>The change was applied.</summary>
    Ok = 0,

    /// <summary>The player is on a map where nothing can be placed, or not in their own place.</summary>
    NotPlaceable = 1,

    /// <summary>The inventory slot is empty, outside the inventory, or holds something that cannot be placed.</summary>
    NoItem = 2,

    /// <summary>The rotation does not exist for this piece.</summary>
    BadRotation = 3,

    /// <summary>Part of the footprint is outside the room.</summary>
    OutsideRoom = 4,

    /// <summary>Part of the footprint is a wall or other fixed obstacle.</summary>
    Blocked = 5,

    /// <summary>Part of the footprint is a tile kept clear (the door).</summary>
    Reserved = 6,

    /// <summary>Another object is already there.</summary>
    Overlaps = 7,

    /// <summary>The player stands too far away.</summary>
    OutOfReach = 8,

    /// <summary>The player stands where the object would go.</summary>
    PlayerInTheWay = 9,

    /// <summary>No such object in this place.</summary>
    UnknownObject = 10,

    /// <summary>The inventory has no room to take the object back.</summary>
    InventoryFull = 11,

    /// <summary>The room holds as many objects as it may.</summary>
    LimitReached = 12,

    /// <summary>Another change to the same object or inventory was applied at the same time; ask again.</summary>
    Conflict = 13,
}
