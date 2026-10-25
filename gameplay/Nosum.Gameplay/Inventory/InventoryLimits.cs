// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Inventory;

/// <summary>Inventory sizes shared by the server, the client and the wire.</summary>
public static class InventoryLimits
{
    /// <summary>Slots a new player's inventory has (4 rows of 9).</summary>
    public const int DefaultCapacity = 36;

    /// <summary>Slots shown in the hotbar: the first slots of the inventory, selected with keys 1 to 9.</summary>
    public const int HotbarSlots = 9;

    /// <summary>Largest inventory a player may ever have; the wire sends slot numbers as one byte.</summary>
    public const int MaxCapacity = 255;
}
