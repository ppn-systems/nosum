// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Dev.S2C;

/// <summary>Reply to <see cref="C2S.DevFreezeClock"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevFreezeClockResult : PacketBase<DevFreezeClockResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevFreezeClockResult;

    /// <summary>True when the clock was pinned or released.</summary>
    [SerializeOrder(0)] public bool Ok { get; set; }

    /// <summary>The server clock after the change, Unix milliseconds; the client re-bases its clock estimate on it.</summary>
    [SerializeOrder(1)] public long ServerNowMs { get; set; }
}
