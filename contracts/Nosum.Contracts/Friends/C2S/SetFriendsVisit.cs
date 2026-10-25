// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Friends.C2S;

/// <summary>Turns friends may visit on or off (docs/adr/ADR-019, ADR-029 D4).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SetFriendsVisit : PacketBase<SetFriendsVisit>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FriendsOpCode.SetFriendsVisit;

    /// <summary>Whether friends may visit the farm without being on the allow-list.</summary>
    [SerializeOrder(0)] public bool Allowed { get; set; }
}
