// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Friends.C2S;

/// <summary>Removes a friend, or withdraws an open request, as listed in the snapshot.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RemoveFriend : PacketBase<RemoveFriend>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FriendsOpCode.RemoveFriend;

    /// <summary>The account id of the other player.</summary>
    [SerializeOrder(0)] public Guid PlayerId { get; set; }
}
