// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Placeable;

/// <summary>Where a furnace stands. Values are append-only.</summary>
public enum FurnaceStateCode : byte
{
    /// <summary>Nothing is smelting and nothing waits to be collected.</summary>
    Idle = 0,

    /// <summary>Smelting; the bar is not ready yet.</summary>
    Working = 1,

    /// <summary>The bar is ready to be collected.</summary>
    Ready = 2,
}

/// <summary>Outcome code of a furnace request. Clients map it to text. Values are append-only.</summary>
public enum FurnaceStatus : byte
{
    /// <summary>The request was applied.</summary>
    Ok = 0,

    /// <summary>No furnace with that id stands in this place.</summary>
    UnknownObject = 1,

    /// <summary>The furnace is not the player's to use: a visitor, or a player who is not a member of the co-op whose land it is.</summary>
    NotYours = 2,

    /// <summary>The player stands too far from the furnace.</summary>
    OutOfReach = 3,

    /// <summary>The furnace already has a job; collect it first.</summary>
    Busy = 4,

    /// <summary>The item is not an ore a furnace smelts.</summary>
    NotSmeltable = 5,

    /// <summary>The player does not have five ore and one coal.</summary>
    NotEnough = 6,

    /// <summary>The furnace holds no finished bar.</summary>
    NothingToCollect = 7,

    /// <summary>The bar is not ready yet.</summary>
    NotReady = 8,

    /// <summary>The inventory has no room for the bar.</summary>
    NoSpace = 9,

    /// <summary>Another change to the same furnace or inventory was applied at the same time; ask again.</summary>
    Conflict = 10,
}
