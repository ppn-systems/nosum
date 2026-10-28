// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Combat.S2C;

/// <summary>Reply to <see cref="C2S.AttackMonster"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AttackMonsterResult : PacketBase<AttackMonsterResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.AttackMonsterResult;

    /// <summary>Outcome of the attack.</summary>
    [SerializeOrder(0)] public CombatStatus Status { get; set; }

    /// <summary>Target monster id.</summary>
    [SerializeOrder(1)] public int MonsterId { get; set; }

    /// <summary>Damage dealt to the monster.</summary>
    [SerializeOrder(2)] public ushort DamageDealt { get; set; }

    /// <summary>Remaining monster HP (0 when defeated).</summary>
    [SerializeOrder(3)] public ushort MonsterHp { get; set; }

    /// <summary>Counter-attack damage taken by the player.</summary>
    [SerializeOrder(4)] public ushort DamageTaken { get; set; }

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

    /// <summary>Whether the player was knocked out and revived at 50% HP.</summary>
    [SerializeOrder(10)] public bool KnockedOut { get; set; }
}
