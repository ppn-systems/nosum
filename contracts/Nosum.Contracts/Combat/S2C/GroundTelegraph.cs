// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Combat.S2C;

/// <summary>Push when a boss telegraphs a ground slam: the struck area is shown until the impact after <see cref="DelayMs"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GroundTelegraph : PacketBase<GroundTelegraph>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.GroundTelegraph;

    /// <summary>Centre tile X of the telegraphed area.</summary>
    [SerializeOrder(0)] public ushort X { get; set; }

    /// <summary>Centre tile Y of the telegraphed area.</summary>
    [SerializeOrder(1)] public ushort Y { get; set; }

    /// <summary>Radius of the struck area in tiles (Chebyshev).</summary>
    [SerializeOrder(2)] public byte RadiusTiles { get; set; }

    /// <summary>Milliseconds from this push until the impact.</summary>
    [SerializeOrder(3)] public ushort DelayMs { get; set; }
}
