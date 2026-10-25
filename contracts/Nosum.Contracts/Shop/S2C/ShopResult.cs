// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Shop.S2C;

/// <summary>Reply to <see cref="C2S.BuyFromShop"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ShopResult : PacketBase<ShopResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ShopOpCode.ShopResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public ShopStatus Status { get; set; }

    /// <summary>Coins spent; zero unless the status is Ok.</summary>
    [SerializeOrder(1)] public long Spent { get; set; }
}
