// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Market.C2S;

/// <summary>Puts an item stack on the market for a price; the items leave the inventory into escrow.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CreateListing : PacketBase<CreateListing>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MarketOpCode.CreateListing;

    /// <summary>Inventory slot.</summary>
    [SerializeOrder(0)] public byte Slot { get; set; }

    /// <summary>The item the player expects in the slot.</summary>
    [SerializeOrder(1)] public int ItemId { get; set; }

    /// <summary>How many of the stack.</summary>
    [SerializeOrder(2)] public int Quantity { get; set; }

    /// <summary>Price for the whole stack, coins.</summary>
    [SerializeOrder(3)] public long Price { get; set; }
}
