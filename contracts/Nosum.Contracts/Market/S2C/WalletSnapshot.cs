// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Market.S2C;

/// <summary>Reply to <see cref="C2S.GetWallet"/>: the coin balance.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class WalletSnapshot : PacketBase<WalletSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MarketOpCode.WalletSnapshot;

    /// <summary>Coins the player holds.</summary>
    [SerializeOrder(0)] public long Coins { get; set; }
}
