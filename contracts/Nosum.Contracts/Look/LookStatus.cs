// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Look;

/// <summary>Outcome code of a look request. Values are append-only.</summary>
public enum LookStatus : byte
{
    /// <summary>Applied.</summary>
    Ok = 0,

    /// <summary>The skin id is not in the catalogue.</summary>
    InvalidSkin = 1,

    /// <summary>The hair style id is not in the catalogue.</summary>
    InvalidHairStyle = 2,

    /// <summary>The hair dye id is not in the catalogue.</summary>
    InvalidHairDye = 3,

    /// <summary>The slot is outside the inventory or holds something else.</summary>
    InvalidSlot = 4,

    /// <summary>The item is not clothing.</summary>
    NotWearable = 5,

    /// <summary>The inventory has no room for the item taken off.</summary>
    NoSpace = 6,

    /// <summary>The player is on no map.</summary>
    NotOnMap = 7,

    /// <summary>Another change happened at the same time; ask again.</summary>
    Conflict = 8,

    /// <summary>Nothing is worn in that slot.</summary>
    NothingWorn = 9,
}
