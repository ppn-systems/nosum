// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Coop.C2S;

/// <summary>Answers an invitation.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RespondInvite : PacketBase<RespondInvite>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CoopOpCode.RespondInvite;

    /// <summary>The invitation.</summary>
    [SerializeOrder(0)] public Guid InviteId { get; set; }

    /// <summary>Whether to accept.</summary>
    [SerializeOrder(1)] public bool Accept { get; set; }
}
