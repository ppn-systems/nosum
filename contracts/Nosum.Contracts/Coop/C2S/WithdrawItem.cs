// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Coop.C2S;

/// <summary>Moves items from the warehouse into the inventory.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class WithdrawItem : PacketBase<WithdrawItem>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CoopOpCode.WithdrawItem;

    /// <summary>Item id.</summary>
    [WireRange(0, WireLimits.MaxId)]
    [SerializeOrder(0)] public int ItemId { get; set; }

    /// <summary>How many.</summary>
    [WireRange(0, WireLimits.MaxQuantity)]
    [SerializeOrder(1)] public int Quantity { get; set; }
}
