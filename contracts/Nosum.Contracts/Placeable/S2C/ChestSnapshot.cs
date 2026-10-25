// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>Reply to <see cref="C2S.OpenChest"/>: the chest's occupied slots, or why it did not open. Sent when a chest is opened, never with the placed objects.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ChestSnapshot : PacketBase<ChestSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.ChestSnapshot;

    /// <summary>Ok, or why the chest did not open.</summary>
    [SerializeOrder(0)] public ChestStatus Status { get; set; }

    /// <summary>Id of the chest.</summary>
    [SerializeOrder(1)] public int ObjectId { get; set; }

    /// <summary>Number of slots.</summary>
    [SerializeOrder(2)] public byte Capacity { get; set; }

    /// <summary>The occupied slots.</summary>
    [SerializeOrder(3)] public List<Nosum.Contracts.Inventory.S2C.InventorySlotRow> Slots { get; set; } = [];
}
