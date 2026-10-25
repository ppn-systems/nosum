// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Player.S2C;

/// <summary>Reply to <see cref="C2S.JoinMap"/>: the player's id and where they stand.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class JoinMapResult : PacketBase<JoinMapResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlayerOpCode.JoinMapResult;

    /// <summary>Outcome code; the other fields are only meaningful when it is <see cref="JoinStatus.Ok"/>.</summary>
    [SerializeOrder(0)] public JoinStatus Status { get; set; }

    /// <summary>Id of the player on this server, unique while the player is connected.</summary>
    [SerializeOrder(1)] public uint PlayerId { get; set; }

    /// <summary>Spawn feet x in world pixels.</summary>
    [SerializeOrder(2)] public float SpawnX { get; set; }

    /// <summary>Spawn feet y in world pixels.</summary>
    [SerializeOrder(3)] public float SpawnY { get; set; }
}
