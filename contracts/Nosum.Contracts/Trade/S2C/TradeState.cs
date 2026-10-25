// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Trade.S2C;

/// <summary>Pushed to both parties whenever the open trade changes.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TradeState : PacketBase<TradeState>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => TradeOpCode.TradeState;

    /// <summary>The other party's id on the map.</summary>
    [SerializeOrder(0)] public uint OtherPlayerId { get; set; }

    /// <summary>The receiver's offered items.</summary>
    [SerializeOrder(1)] public List<TradeItemRow> MyItems { get; set; } = [];

    /// <summary>The receiver's offered coins.</summary>
    [SerializeOrder(2)] public long MyCoins { get; set; }

    /// <summary>The other party's offered items.</summary>
    [SerializeOrder(3)] public List<TradeItemRow> OtherItems { get; set; } = [];

    /// <summary>The other party's offered coins.</summary>
    [SerializeOrder(4)] public long OtherCoins { get; set; }

    /// <summary>Whether the receiver confirmed.</summary>
    [SerializeOrder(5)] public bool MyConfirmed { get; set; }

    /// <summary>Whether the other party confirmed.</summary>
    [SerializeOrder(6)] public bool OtherConfirmed { get; set; }
}
