// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Friends.C2S;

/// <summary>Sends a friend request. If the other player already asked, the two become friends.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RequestFriend : PacketBase<RequestFriend>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FriendsOpCode.RequestFriend;

    /// <summary>The other player's nickname.</summary>
    [SerializeOrder(0)] public string Nickname { get; set; } = string.Empty;
}
