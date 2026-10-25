// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Coop.C2S;

/// <summary>Invites a player on the same map.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class InvitePlayer : PacketBase<InvitePlayer>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CoopOpCode.InvitePlayer;

    /// <summary>The other player's id on the map.</summary>
    [SerializeOrder(0)] public uint TargetPlayerId { get; set; }
}
