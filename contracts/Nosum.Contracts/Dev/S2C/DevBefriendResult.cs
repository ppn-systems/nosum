// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Dev.S2C;

/// <summary>Reply to <see cref="C2S.DevBefriend"/>. When <see cref="Ok"/> is false the other fields are empty.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevBefriendResult : PacketBase<DevBefriendResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevBefriendResult;

    /// <summary>True when the test player was made and befriended.</summary>
    [SerializeOrder(0)] public bool Ok { get; set; }

    /// <summary>The new test player's nickname.</summary>
    [SerializeOrder(1)] public string Nickname { get; set; } = string.Empty;

    /// <summary>A session token for the new test player; a second client resumes as that player with it.</summary>
    [SerializeOrder(2)] public string SessionToken { get; set; } = string.Empty;
}
