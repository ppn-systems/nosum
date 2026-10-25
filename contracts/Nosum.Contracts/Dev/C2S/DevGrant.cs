// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Dev.C2S;

/// <summary>Development only: asks the server to give the sender an item stack and/or coins. Not handled in Production.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevGrant : PacketBase<DevGrant>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevGrant;

    /// <summary>The item to give, or zero for coins only.</summary>
    [WireRange(0, WireLimits.MaxId)]
    [SerializeOrder(0)] public int ItemId { get; set; }

    /// <summary>How many items, 1 to a stack; ignored when <see cref="ItemId"/> is zero.</summary>
    [WireRange(0, WireLimits.MaxQuantity)]
    [SerializeOrder(1)] public int Quantity { get; set; }

    /// <summary>Coins to add; zero for none.</summary>
    [WireRange(0, WireLimits.MaxCoins)]
    [SerializeOrder(2)] public long Coins { get; set; }
}
