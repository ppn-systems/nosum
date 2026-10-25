// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Inventory.S2C;

/// <summary>Reply to <see cref="C2S.GetInventory"/>: the whole inventory.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class InventorySnapshot : PacketBase<InventorySnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => InventoryOpCode.InventorySnapshot;

    /// <summary>Number of slots the inventory has.</summary>
    [SerializeOrder(0)] public byte Capacity { get; set; }

    /// <summary>The hotbar slot the player holds.</summary>
    [SerializeOrder(1)] public byte SelectedSlot { get; set; }

    /// <summary>The slots that hold something; the others are empty.</summary>
    [SerializeOrder(2)] public List<InventorySlotRow> Slots { get; set; } = [];
}
