// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Shop.C2S;

/// <summary>Buys an item from a shop. The server prices it, checks the player stands at the shop, and moves coins and items in one transaction.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class BuyFromShop : PacketBase<BuyFromShop>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ShopOpCode.BuyFromShop;

    /// <summary>The shop id (<c>ShopCatalog</c>).</summary>
    [WireRange(0, WireLimits.MaxId)]
    [SerializeOrder(0)] public int ShopId { get; set; }

    /// <summary>The item to buy.</summary>
    [WireRange(0, WireLimits.MaxId)]
    [SerializeOrder(1)] public int ItemId { get; set; }

    /// <summary>How many, 1 to a stack.</summary>
    [WireRange(0, WireLimits.MaxQuantity)]
    [SerializeOrder(2)] public int Quantity { get; set; }
}
