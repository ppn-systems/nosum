// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Combat.S2C;

/// <summary>The player's hunting log: the kills of every species the player has hunted, and the equipped slayer title (docs/adr/ADR-068).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class BestiarySnapshot : PacketBase<BestiarySnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.BestiarySnapshot;

    /// <summary>The equipped title's species, or zero for none.</summary>
    [SerializeOrder(0)] public byte EquippedSpecies { get; set; }

    /// <summary>The kill counts, one row per species that has been hunted.</summary>
    [SerializeOrder(1)] public List<BestiaryRow> Rows { get; set; } = [];
}
