// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Placeable;

/// <summary>Where a stack is, in a <see cref="C2S.ChestMove"/>. Values are append-only.</summary>
public enum ChestSide : byte
{
    /// <summary>The player's own inventory.</summary>
    Player = 0,

    /// <summary>The open chest.</summary>
    Chest = 1,
}

/// <summary>Outcome code of a chest request. Clients map it to text. Values are append-only.</summary>
public enum ChestStatus : byte
{
    /// <summary>The request was applied.</summary>
    Ok = 0,

    /// <summary>No chest with that id stands in this place.</summary>
    UnknownObject = 1,

    /// <summary>The chest is not the player's to open: a visitor, or a player who is not a member of the co-op whose land it is.</summary>
    NotYours = 2,

    /// <summary>The player stands too far from the chest.</summary>
    OutOfReach = 3,

    /// <summary>The chest is not open for this player; open it first.</summary>
    NotOpen = 4,

    /// <summary>A slot number is outside the inventory or the chest.</summary>
    InvalidSlot = 5,

    /// <summary>The source slot holds nothing.</summary>
    EmptySlot = 6,

    /// <summary>The quantity is larger than the stack.</summary>
    InvalidQuantity = 7,

    /// <summary>The target slot holds another item and a part of a stack cannot swap with it.</summary>
    Blocked = 8,

    /// <summary>There is no room for the stack.</summary>
    NoSpace = 9,

    /// <summary>Another change to the same chest or inventory was applied at the same time; ask again.</summary>
    Conflict = 10,

    /// <summary>The item cannot go into a chest (a tool).</summary>
    NotAllowed = 11,
}
