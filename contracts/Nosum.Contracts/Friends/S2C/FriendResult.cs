// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Friends.S2C;

/// <summary>Reply to a friends request.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class FriendResult : PacketBase<FriendResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FriendsOpCode.FriendResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public FriendStatus Status { get; set; }
}
