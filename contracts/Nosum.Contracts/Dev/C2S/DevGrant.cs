// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

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
    [SerializeOrder(0)] public int ItemId { get; set; }

    /// <summary>How many items, 1 to a stack; ignored when <see cref="ItemId"/> is zero.</summary>
    [SerializeOrder(1)] public int Quantity { get; set; }

    /// <summary>Coins to add; zero for none.</summary>
    [SerializeOrder(2)] public long Coins { get; set; }
}
