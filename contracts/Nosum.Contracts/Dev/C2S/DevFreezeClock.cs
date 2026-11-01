// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Dev.C2S;

/// <summary>Development only: pins the server's game clock (or releases it), so a QA run keeps the season and the day it started in. Not handled in Production.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevFreezeClock : PacketBase<DevFreezeClock>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevFreezeClock;

    /// <summary>True to pin the clock at this instant; false to let it run again from the same instant.</summary>
    [SerializeOrder(0)] public bool Frozen { get; set; }
}
