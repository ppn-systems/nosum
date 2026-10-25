// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Friends.C2S;

/// <summary>Accepts or declines a friend request.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RespondFriend : PacketBase<RespondFriend>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FriendsOpCode.RespondFriend;

    /// <summary>The account id of the player who asked, as listed in the snapshot.</summary>
    [SerializeOrder(0)] public Guid PlayerId { get; set; }

    /// <summary>Whether to accept.</summary>
    [SerializeOrder(1)] public bool Accept { get; set; }
}
