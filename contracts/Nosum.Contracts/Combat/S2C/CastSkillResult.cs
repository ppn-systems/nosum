// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Combat.S2C;

/// <summary>Reply to <see cref="C2S.CastSkill"/> (docs/adr/ADR-056).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CastSkillResult : PacketBase<CastSkillResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.CastSkillResult;

    /// <summary>Outcome of the skill execution.</summary>
    [SerializeOrder(0)] public CombatStatus Status { get; set; }

    /// <summary>Skill kind that was executed.</summary>
    [SerializeOrder(1)] public byte SkillId { get; set; }

    /// <summary>Total damage dealt across targets.</summary>
    [SerializeOrder(2)] public ushort DamageDealt { get; set; }

    /// <summary>Number of monsters struck.</summary>
    [SerializeOrder(3)] public byte TargetsHit { get; set; }

    /// <summary>HP healed by restoration skills.</summary>
    [SerializeOrder(4)] public ushort HealAmount { get; set; }

    /// <summary>Updated player HP.</summary>
    [SerializeOrder(5)] public ushort Hp { get; set; }

    /// <summary>Updated player MaxHp.</summary>
    [SerializeOrder(6)] public ushort MaxHp { get; set; }

    /// <summary>Updated player MP.</summary>
    [SerializeOrder(7)] public ushort Mp { get; set; }

    /// <summary>Updated player MaxMp.</summary>
    [SerializeOrder(8)] public ushort MaxMp { get; set; }

    /// <summary>Updated cumulative Combat XP.</summary>
    [SerializeOrder(9)] public int CombatXp { get; set; }

    /// <summary>Cooldown duration in milliseconds.</summary>
    [SerializeOrder(10)] public ushort CooldownMs { get; set; }

    /// <summary>Active combat combo streak count (docs/adr/ADR-060).</summary>
    [SerializeOrder(11)] public byte ComboCount { get; set; }
}
