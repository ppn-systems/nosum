// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Inventory;

/// <summary>
/// Opcodes of the Inventory domain.
/// <para>Band <c>0x5000-0x50FF</c> is reserved for this domain. C2S requests use <c>0x5000-0x503F</c>,
/// S2C replies and pushes use <c>0x5040-0x507F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class InventoryOpCode
{
    // -- C2S (0x5000-0x503F) --------------------------------------------------

    /// <summary>Asks for the whole inventory. Reply: <see cref="S2C.InventorySnapshot"/>.</summary>
    public const ushort GetInventory = 0x5000;

    /// <summary>Moves, splits, merges or swaps stacks. Reply: <see cref="S2C.InventoryResult"/>; the changed slots follow as <see cref="S2C.InventoryUpdate"/>.</summary>
    public const ushort MoveSlot = 0x5001;

    /// <summary>Selects the hotbar slot the player holds. Reply: <see cref="S2C.InventoryResult"/>.</summary>
    public const ushort SelectSlot = 0x5002;

    /// <summary>Sorts the player's inventory: merges partial stacks and lays the stacks out by item kind and id. Reply: <see cref="S2C.InventoryResult"/>; the changed slots follow as <see cref="S2C.InventoryUpdate"/>.</summary>
    public const ushort SortInventory = 0x5003;

    // -- S2C (0x5040-0x507F) --------------------------------------------------

    /// <summary>Reply to <see cref="GetInventory"/>.</summary>
    public const ushort InventorySnapshot = 0x5040;

    /// <summary>Reply to <see cref="MoveSlot"/> and <see cref="SelectSlot"/>.</summary>
    public const ushort InventoryResult = 0x5041;

    /// <summary>Push to one player: some slots changed (any feature may cause it: farming, market, quests).</summary>
    public const ushort InventoryUpdate = 0x5042;
}
