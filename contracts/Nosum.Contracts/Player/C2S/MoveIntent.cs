// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Player.C2S;

/// <summary>
/// Reports the player's feet position, facing and whether they are walking. Sent about 10 times
/// per second; the server checks speed, bounds and collision before it accepts it.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MoveIntent : PacketBase<MoveIntent>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlayerOpCode.MoveIntent;

    /// <summary>Client counter, echoed in <see cref="S2C.MoveCorrection"/> so the client can tell which intent was refused.</summary>
    [SerializeOrder(0)] public uint Sequence { get; set; }

    /// <summary>Feet x in world pixels.</summary>
    [SerializeOrder(1)] public float X { get; set; }

    /// <summary>Feet y in world pixels.</summary>
    [SerializeOrder(2)] public float Y { get; set; }

    /// <summary>Facing: 0 down, 1 right, 2 up, 3 left.</summary>
    [SerializeOrder(3)] public byte Facing { get; set; }

    /// <summary>Whether the player is walking.</summary>
    [SerializeOrder(4)] public bool Moving { get; set; }
}
