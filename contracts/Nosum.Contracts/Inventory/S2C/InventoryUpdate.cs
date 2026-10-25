// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Inventory.S2C;

/// <summary>Pushed to one player after the server changed their inventory: only the slots that changed, each in its new state.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class InventoryUpdate : PacketBase<InventoryUpdate>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => InventoryOpCode.InventoryUpdate;

    /// <summary>The changed slots; a row with item id zero means the slot is now empty.</summary>
    [SerializeOrder(0)] public List<InventorySlotRow> Slots { get; set; } = [];
}
