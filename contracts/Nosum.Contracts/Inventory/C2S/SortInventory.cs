// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Inventory.C2S;

/// <summary>Sorts the signed-in player's inventory. The server decides the layout; the request says only whether the hotbar stays as it is.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SortInventory : PacketBase<SortInventory>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => InventoryOpCode.SortInventory;

    /// <summary>Reserved; always zero.</summary>
    [SerializeOrder(0)] public byte Reserved { get; set; }

    /// <summary>Whether the hotbar slots keep their contents while the rest of the bag is sorted. Nonzero locks it.</summary>
    [SerializeOrder(1)] public bool LockHotbar { get; set; }
}
