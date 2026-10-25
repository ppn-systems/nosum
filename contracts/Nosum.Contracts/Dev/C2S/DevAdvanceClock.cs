// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Dev.C2S;

/// <summary>Development only: asks the server to move its game clock forward by whole game days, so crop growth and day rollovers can be tested. Not handled in Production.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevAdvanceClock : PacketBase<DevAdvanceClock>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevAdvanceClock;

    /// <summary>How many game days to skip, 1 to 60.</summary>
    [WireRange(0, 365)]
    [SerializeOrder(0)] public int Days { get; set; }
}
