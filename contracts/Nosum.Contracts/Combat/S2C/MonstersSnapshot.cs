// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Combat.S2C;

/// <summary>Every live monster on the player's current map or mine floor.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MonstersSnapshot : PacketBase<MonstersSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.MonstersSnapshot;

    /// <summary>The map id the monsters belong to.</summary>
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;

    /// <summary>Live monsters on the map.</summary>
    [SerializeOrder(1)] public List<MonsterRow> Monsters { get; set; } = [];

    /// <summary>Environmental hazards on the map (docs/adr/ADR-061).</summary>
    [SerializeOrder(2)] public List<HazardRow> Hazards { get; set; } = [];
}
