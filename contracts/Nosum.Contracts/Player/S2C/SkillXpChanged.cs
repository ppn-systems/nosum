// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Player.S2C;

/// <summary>Pushed to one player when skill experience changes and once after joining: the cumulative totals; the client derives levels from the same table.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SkillXpChanged : PacketBase<SkillXpChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlayerOpCode.SkillXpChanged;

    /// <summary>Cumulative farming experience.</summary>
    [SerializeOrder(0)] public int FarmingXp { get; set; }

    /// <summary>Cumulative foraging experience.</summary>
    [SerializeOrder(1)] public int ForagingXp { get; set; }

    /// <summary>Cumulative mining experience.</summary>
    [SerializeOrder(2)] public int MiningXp { get; set; }
}
