// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Node.S2C;

/// <summary>Pushed to one player after the watering can was used or refilled.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ToolState : PacketBase<ToolState>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => NodeOpCode.ToolState;

    /// <summary>Water left in the watering can.</summary>
    [SerializeOrder(0)] public byte WaterLeft { get; set; }

    /// <summary>Largest amount the can holds.</summary>
    [SerializeOrder(1)] public byte WaterCapacity { get; set; }
}
