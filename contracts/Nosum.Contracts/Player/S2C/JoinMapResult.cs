// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Player.S2C;

/// <summary>Reply to <see cref="C2S.JoinMap"/>: the player's id, the map the server put them on and where they stand.</summary>
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

    /// <summary>Stable id of the map the server put the player on: where they were last, else the starting map.</summary>
    [SerializeOrder(4)] public string MapId { get; set; } = string.Empty;

    /// <summary>Facing at the spawn: 0 down, 1 right, 2 up, 3 left.</summary>
    [SerializeOrder(5)] public byte Facing { get; set; }

    /// <summary>Gets or sets the community unlocks that change what the client may walk on (<c>CommunityUnlockSet</c>, docs/adr/ADR-037 D4); appended to the packet.</summary>
    [SerializeOrder(6)] public byte Unlocks { get; set; }
}
