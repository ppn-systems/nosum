// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Inventory.C2S;

/// <summary>Sorts the signed-in player's inventory. The server decides the layout; the request carries no choice.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SortInventory : PacketBase<SortInventory>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => InventoryOpCode.SortInventory;

    /// <summary>Reserved; the request carries no data.</summary>
    [SerializeOrder(0)] public byte Reserved { get; set; }
}
