// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Look.S2C;

/// <summary>Pushed to every player of a map when a player's look is announced or changes.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class LookChanged : PacketBase<LookChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => LookOpCode.LookChanged;

    /// <summary>The look.</summary>
    [SerializeOrder(0)] public LookRow Look { get; set; } = new();
}
