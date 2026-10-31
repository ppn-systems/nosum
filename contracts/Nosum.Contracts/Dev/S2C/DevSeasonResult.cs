// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Dev.S2C;

/// <summary>Reply to DevSeason.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevSeasonResult : PacketBase<DevSeasonResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevSeasonResult;

    /// <summary>True when the request was applied.</summary>
    [SerializeOrder(0)] public bool Ok { get; set; }
}
