// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Coop.C2S;

/// <summary>Moves items from the inventory into the warehouse.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DepositItem : PacketBase<DepositItem>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CoopOpCode.DepositItem;

    /// <summary>Item id.</summary>
    [SerializeOrder(0)] public int ItemId { get; set; }

    /// <summary>How many.</summary>
    [SerializeOrder(1)] public int Quantity { get; set; }
}
