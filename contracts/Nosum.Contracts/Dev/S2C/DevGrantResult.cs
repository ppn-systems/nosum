// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Dev.S2C;

/// <summary>Reply to <see cref="C2S.DevGrant"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevGrantResult : PacketBase<DevGrantResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevGrantResult;

    /// <summary>True when the grant was applied.</summary>
    [SerializeOrder(0)] public bool Ok { get; set; }
}
