// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Combat;

/// <summary>Identifies a special combat skill (docs/adr/ADR-056).</summary>
public enum CombatSkillKind : byte
{
    /// <summary>No skill.</summary>
    None = 0,

    /// <summary>Whirlwind Cleave: Strikes all monsters within 2 tiles radius.</summary>
    Whirlwind = 1,

    /// <summary>Fireball: Launches high-damage pyromancy projectile up to 6 tiles.</summary>
    Fireball = 2,

    /// <summary>Divine Heal: Chants holy restoration healing player HP.</summary>
    DivineHeal = 3,
}

/// <summary>Immutable specification of a combat skill.</summary>
/// <param name="Kind">The combat skill kind.</param>
/// <param name="Key">Unique string identifier.</param>
/// <param name="MpCost">Required MP cost.</param>
/// <param name="CooldownMs">Cooldown period in milliseconds.</param>
/// <param name="BasePower">Base damage or healing power.</param>
/// <param name="Range">Maximum tile range (0 for self/AoE).</param>
/// <param name="IsAoe">Whether the skill affects multiple nearby monsters.</param>
public sealed record SkillDefinition(
    CombatSkillKind Kind,
    string Key,
    int MpCost,
    int CooldownMs,
    int BasePower,
    int Range,
    bool IsAoe);

/// <summary>Pure catalog of combat skills with deterministic costs and cooldowns (docs/adr/ADR-056).</summary>
public static class SkillCatalog
{
    /// <summary>Whirlwind Cleave skill.</summary>
    public static readonly SkillDefinition Whirlwind = new(CombatSkillKind.Whirlwind, "whirlwind", MpCost: 15, CooldownMs: 3000, BasePower: 18, Range: 2, IsAoe: true);

    /// <summary>Fireball Pyromancy skill.</summary>
    public static readonly SkillDefinition Fireball = new(CombatSkillKind.Fireball, "fireball", MpCost: 25, CooldownMs: 4000, BasePower: 32, Range: 6, IsAoe: false);

    /// <summary>Divine Heal skill.</summary>
    public static readonly SkillDefinition DivineHeal = new(CombatSkillKind.DivineHeal, "divine_heal", MpCost: 20, CooldownMs: 6000, BasePower: 35, Range: 0, IsAoe: false);

    /// <summary>Finds a skill definition by its kind.</summary>
    public static bool TryGet(CombatSkillKind kind, out SkillDefinition definition)
    {
        switch (kind)
        {
            case CombatSkillKind.Whirlwind:
                definition = Whirlwind;
                return true;
            case CombatSkillKind.Fireball:
                definition = Fireball;
                return true;
            case CombatSkillKind.DivineHeal:
                definition = DivineHeal;
                return true;
            default:
                definition = default!;
                return false;
        }
    }
}
