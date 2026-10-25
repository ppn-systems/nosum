// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Inventory.C2S;

/// <summary>Selects the hotbar slot the player holds; the server remembers it for the next session.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SelectSlot : PacketBase<SelectSlot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => InventoryOpCode.SelectSlot;

    /// <summary>Hotbar slot, zero to eight.</summary>
    [SerializeOrder(0)] public byte Slot { get; set; }
}
