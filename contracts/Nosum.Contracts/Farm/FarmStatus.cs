// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Farm;

/// <summary>Outcome code of a <see cref="C2S.UseItem"/> request. Clients map it to text. Values are append-only.</summary>
public enum FarmStatus : byte
{
    /// <summary>The action was applied.</summary>
    Ok = 0,

    /// <summary>The player is not on a map that can be farmed.</summary>
    NotOnFarm = 1,

    /// <summary>The hotbar slot is outside the hotbar.</summary>
    InvalidSlot = 2,

    /// <summary>The tile is outside the map.</summary>
    InvalidTile = 3,

    /// <summary>The player stands too far from the tile.</summary>
    OutOfReach = 4,

    /// <summary>The ground there cannot be tilled.</summary>
    NotTillable = 5,

    /// <summary>The soil is tilled already.</summary>
    AlreadyTilled = 6,

    /// <summary>The soil must be tilled first.</summary>
    NotTilled = 7,

    /// <summary>A crop already grows there.</summary>
    HasCrop = 8,

    /// <summary>The held item does nothing on that tile (nothing held, or a crop item).</summary>
    NothingToDo = 9,

    /// <summary>The crop is not ready to harvest.</summary>
    NotMature = 10,

    /// <summary>The inventory has no room for the harvest.</summary>
    InventoryFull = 11,

    /// <summary>The seed or tool was no longer in the slot when the action was applied.</summary>
    ItemMissing = 12,

    /// <summary>Another change to the same tile or inventory was applied at the same time; ask again.</summary>
    Conflict = 13,

    /// <summary>The seed's crop does not grow in the current season (docs/adr/ADR-018).</summary>
    OutOfSeason = 14,

    /// <summary>The crop withered when its season ended and cannot be harvested; plant over it.</summary>
    Withered = 15,

    /// <summary>A visitor may only water another player's crops; till, plant and harvest are the owner's (docs/adr/ADR-019 D3).</summary>
    NotYourFarm = 16,

    /// <summary>The held tool does not work on what is on that tile (an axe on a stone).</summary>
    WrongTool = 17,

    /// <summary>The previous tool swing is not over yet (server-side cooldown, docs/adr/ADR-021 D8).</summary>
    Cooldown = 18,

    /// <summary>The watering can is empty; refill it at water.</summary>
    CanEmpty = 19,

    /// <summary>Something is in the way: a node on the tile, or a tile a tree cannot grow on.</summary>
    Blocked = 20,
}

/// <summary>What a successful <see cref="C2S.UseItem"/> did. Values are append-only.</summary>
public enum FarmAction : byte
{
    /// <summary>Nothing happened (the request was refused).</summary>
    None = 0,

    /// <summary>The soil was tilled.</summary>
    Tilled = 1,

    /// <summary>A seed was planted.</summary>
    Planted = 2,

    /// <summary>The soil was watered.</summary>
    Watered = 3,

    /// <summary>A crop was harvested.</summary>
    Harvested = 4,

    /// <summary>A resource node was hit and still stands.</summary>
    Hit = 5,

    /// <summary>A resource node broke (a tree falls to a stump, a stone is gone) and its yield was delivered.</summary>
    Broke = 6,

    /// <summary>The watering can was refilled at water.</summary>
    Refilled = 7,

    /// <summary>Tilled soil was cleared with a pickaxe.</summary>
    Cleared = 8,

    /// <summary>A tree seed was planted.</summary>
    PlantedTree = 9,
}
