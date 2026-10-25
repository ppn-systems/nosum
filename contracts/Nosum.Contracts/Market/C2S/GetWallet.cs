// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Market.C2S;

/// <summary>Asks for the coin balance.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetWallet : PacketBase<GetWallet>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MarketOpCode.GetWallet;
}
