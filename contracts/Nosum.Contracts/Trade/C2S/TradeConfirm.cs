// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Trade.C2S;

/// <summary>Confirms the offers as they stand.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TradeConfirm : PacketBase<TradeConfirm>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => TradeOpCode.TradeConfirm;
}
