// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Farm;

/// <summary>Result of a farm rule. A refusal changes nothing.</summary>
public enum FarmVerdict
{
    /// <summary>The action is allowed.</summary>
    Ok = 0,

    /// <summary>The tile is outside the map.</summary>
    InvalidTile = 1,

    /// <summary>The player stands too far from the tile.</summary>
    OutOfReach = 2,

    /// <summary>The ground there cannot be tilled (not diggable, or blocked by a building, tree or water).</summary>
    NotTillable = 3,

    /// <summary>The soil is tilled already.</summary>
    AlreadyTilled = 4,

    /// <summary>The soil must be tilled first.</summary>
    NotTilled = 5,

    /// <summary>A crop already grows there.</summary>
    HasCrop = 6,

    /// <summary>The held item does nothing on this tile.</summary>
    NothingToDo = 7,

    /// <summary>The crop is not ready yet.</summary>
    NotMature = 8,

    /// <summary>The crop id is not in the catalogue.</summary>
    UnknownCrop = 9,

    /// <summary>The crop does not grow in the current season (docs/adr/ADR-018).</summary>
    OutOfSeason = 10,

    /// <summary>The crop withered when its season ended; plant over it to clear it.</summary>
    Withered = 11,
}
