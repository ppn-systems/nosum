// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Friends.S2C;

/// <summary>The receiver's friends, open requests and visiting setting.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class FriendsSnapshot : PacketBase<FriendsSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FriendsOpCode.FriendsSnapshot;

    /// <summary>Whether friends may visit without being on the allow-list.</summary>
    [SerializeOrder(0)] public bool FriendsMayVisit { get; set; }

    /// <summary>Whether the receiver is a guest, who keeps no friends list.</summary>
    [SerializeOrder(1)] public bool IsGuest { get; set; }

    /// <summary>Mutual friends, by nickname.</summary>
    [SerializeOrder(2)] public List<FriendRow> Friends { get; set; } = [];

    /// <summary>Requests waiting for the receiver's answer.</summary>
    [SerializeOrder(3)] public List<FriendRequestRow> Incoming { get; set; } = [];

    /// <summary>Requests the receiver sent and nobody answered.</summary>
    [SerializeOrder(4)] public List<FriendRequestRow> Outgoing { get; set; } = [];
}
