// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Inventory.S2C;

/// <summary>One slot inside an <see cref="InventorySnapshot"/> or <see cref="InventoryUpdate"/>. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class InventorySlotRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static InventorySlotRow Create() => new();

    /// <summary>Slot number, zero based. The first nine slots are the hotbar.</summary>
    [SerializeOrder(0)] public byte Slot { get; set; }

    /// <summary>Item id from the item catalogue; zero means the slot is empty.</summary>
    [SerializeOrder(1)] public int ItemId { get; set; }

    /// <summary>How many items are stacked; zero for an empty slot.</summary>
    [SerializeOrder(2)] public int Quantity { get; set; }
}
