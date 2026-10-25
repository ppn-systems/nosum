// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Look.C2S;

/// <summary>Wears a clothing item; the item leaves the inventory and the item worn before goes back into it.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EquipItem : PacketBase<EquipItem>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => LookOpCode.EquipItem;

    /// <summary>Inventory slot.</summary>
    [SerializeOrder(0)] public byte Slot { get; set; }

    /// <summary>The item the player expects in the slot.</summary>
    [SerializeOrder(1)] public int ItemId { get; set; }
}
