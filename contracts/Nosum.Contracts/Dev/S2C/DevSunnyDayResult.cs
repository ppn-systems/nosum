// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Dev.S2C;

/// <summary>Reply to <see cref="C2S.DevSunnyDay"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevSunnyDayResult : PacketBase<DevSunnyDayResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevSunnyDayResult;

    /// <summary>True when the clock was moved (or was already on a sunny day).</summary>
    [SerializeOrder(0)] public bool Ok { get; set; }

    /// <summary>The server clock after the move, Unix milliseconds; the client re-bases its clock estimate on it.</summary>
    [SerializeOrder(1)] public long ServerNowMs { get; set; }
}
