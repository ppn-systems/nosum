// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Combat.C2S;

/// <summary>Casts a special combat skill (Whirlwind, Fireball, Divine Heal). Reply: <see cref="S2C.CastSkillResult"/> (docs/adr/ADR-056).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CastSkill : PacketBase<CastSkill>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.CastSkill;

    /// <summary>Skill kind (1 = Whirlwind, 2 = Fireball, 3 = DivineHeal).</summary>
    [SerializeOrder(0)] public byte SkillId { get; set; }

    /// <summary>Target monster id on the map (0 for AoE/Self skills).</summary>
    [WireRange(0, WireLimits.MaxId)]
    [SerializeOrder(1)] public int TargetMonsterId { get; set; }
}
