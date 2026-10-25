// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Player.S2C;

/// <summary>Pushed to one player when a <see cref="C2S.MoveIntent"/> was refused: the position the server keeps.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MoveCorrection : PacketBase<MoveCorrection>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlayerOpCode.MoveCorrection;

    /// <summary><see cref="C2S.MoveIntent.Sequence"/> of the refused intent.</summary>
    [SerializeOrder(0)] public uint Sequence { get; set; }

    /// <summary>Why it was refused.</summary>
    [SerializeOrder(1)] public MoveRefusal Reason { get; set; }

    /// <summary>Feet x the server keeps, world pixels.</summary>
    [SerializeOrder(2)] public float X { get; set; }

    /// <summary>Feet y the server keeps, world pixels.</summary>
    [SerializeOrder(3)] public float Y { get; set; }
}
