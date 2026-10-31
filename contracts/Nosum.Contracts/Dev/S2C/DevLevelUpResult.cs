// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Dev.S2C;

/// <summary>Reply to DevLevelUp.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevLevelUpResult : PacketBase<DevLevelUpResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevLevelUpResult;

    /// <summary>True when the request was applied.</summary>
    [SerializeOrder(0)] public bool Ok { get; set; }
}
