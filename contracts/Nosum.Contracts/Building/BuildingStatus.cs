// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Building;

/// <summary>
/// The outcome of a building or animal request (docs/adr/ADR-028). The client maps each value to a text; never reorder or reuse a value.
/// </summary>
public enum BuildingStatus : byte
{
    /// <summary>It worked.</summary>
    Ok = 0,

    /// <summary>The player is not at the carpenter's counter.</summary>
    NotAtShop = 1,

    /// <summary>The kind, building or animal is not known (or not in the place the player is).</summary>
    Unknown = 2,

    /// <summary>Not enough coins.</summary>
    NotEnoughCoins = 3,

    /// <summary>Not enough wood or stone, or the item to put in is not in the slot.</summary>
    NotEnoughItems = 4,

    /// <summary>The inventory has no room.</summary>
    NoSpace = 5,

    /// <summary>Somebody else changed it first; try again.</summary>
    Conflict = 6,

    /// <summary>The building or animal is not the player's to tend.</summary>
    NotYours = 7,

    /// <summary>The building is still under construction.</summary>
    NotFinished = 8,

    /// <summary>The building is full.</summary>
    Full = 9,

    /// <summary>That species does not live in that kind of building.</summary>
    WrongResident = 10,

    /// <summary>The name is not allowed.</summary>
    BadName = 11,

    /// <summary>The player is not standing in the building.</summary>
    NotInside = 12,

    /// <summary>The item is not feed.</summary>
    NotFeed = 13,

    /// <summary>The player is too far from the animal or the feeding bench.</summary>
    OutOfReach = 14,

    /// <summary>The animal was already petted today.</summary>
    AlreadyPetted = 15,

    /// <summary>The animal has nothing to give today.</summary>
    NoProduce = 16,

    /// <summary>A cow needs the milk pail in hand.</summary>
    NeedsPail = 17,

    /// <summary>The feeding bench is full.</summary>
    BenchFull = 18,

    /// <summary>The land already holds as many buildings as it may.</summary>
    LimitReached = 19,
}
