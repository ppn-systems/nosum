// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Admin.C2S;

/// <summary>Admin intent: gives a player items or coins through the one transfer service.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AdminGrantItem : PacketBase<AdminGrantItem>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AdminOpCode.GrantItem;

    /// <summary>The nickname of the player who receives the grant.</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(0)] public string Nickname { get; set; } = string.Empty;

    /// <summary>The item to give, or zero for a coin-only grant.</summary>
    [WireRange(0, WireLimits.MaxId)]
    [SerializeOrder(1)] public int ItemId { get; set; }

    /// <summary>How many items (at most one stack).</summary>
    [WireRange(0, WireLimits.MaxQuantity)]
    [SerializeOrder(2)] public int Quantity { get; set; }

    /// <summary>How many coins to give; zero for none.</summary>
    [WireRange(0, WireLimits.MaxCoins)]
    [SerializeOrder(3)] public long Coins { get; set; }
}
