// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Market.C2S;

/// <summary>Sells items from an inventory slot at the shipping bin; the server checks the player stands at the bin and prices the item itself.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ShipItems : PacketBase<ShipItems>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MarketOpCode.ShipItems;

    /// <summary>Inventory slot.</summary>
    [SerializeOrder(0)] public byte Slot { get; set; }

    /// <summary>The item the player expects in the slot.</summary>
    [SerializeOrder(1)] public int ItemId { get; set; }

    /// <summary>How many; zero or less ships the whole stack.</summary>
    [SerializeOrder(2)] public int Quantity { get; set; }
}
