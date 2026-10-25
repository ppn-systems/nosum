// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Friends.S2C;

/// <summary>Pushed to a player: a mutual friend went online or offline or changed public map.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class FriendStatusChanged : PacketBase<FriendStatusChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FriendsOpCode.FriendStatusChanged;

    /// <summary>The friend's account id.</summary>
    [SerializeOrder(0)] public Guid PlayerId { get; set; }

    /// <summary>Whether the friend is online.</summary>
    [SerializeOrder(1)] public bool Online { get; set; }

    /// <summary>The public map the friend is on; empty when offline or in a private place.</summary>
    [SerializeOrder(2)] public string MapId { get; set; } = string.Empty;
}
