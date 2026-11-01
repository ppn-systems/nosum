// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Combat;

/// <summary>
/// Opcodes of the Combat domain: player HP &amp; MP vitals, monster spawns, weapon/staff attacks, and potion drinking
/// (docs/adr/ADR-045 D3), and the hunting log with its slayer titles (docs/adr/ADR-068).
/// <para>Band <c>0xC200-0xC2FF</c> is reserved for this domain, next to the Mine band (<c>0xC100-0xC1FF</c>). C2S requests use <c>0xC200-0xC23F</c>, S2C replies and pushes use <c>0xC240-0xC27F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class CombatOpCode
{
    // -- C2S (0xC200-0xC23F) --------------------------------------------------

    /// <summary>Asks for the player's current HP, MaxHp, MP, MaxMp and Combat XP. Reply: <see cref="S2C.VitalsState"/>.</summary>
    public const ushort GetVitals = 0xC200;

    /// <summary>Asks for the monsters on the player's current map or mine floor. Reply: <see cref="S2C.MonstersSnapshot"/>.</summary>
    public const ushort GetMonsters = 0xC201;

    /// <summary>Strikes or casts at a monster on the player's current map. Reply: <see cref="S2C.AttackMonsterResult"/>.</summary>
    public const ushort AttackMonster = 0xC202;

    /// <summary>Drinks a potion from a hotbar/inventory slot to restore HP and MP. Reply: <see cref="S2C.DrinkPotionResult"/>.</summary>
    public const ushort DrinkPotion = 0xC203;

    /// <summary>Casts a combat skill (Whirlwind, Fireball, Divine Heal). Reply: <see cref="S2C.CastSkillResult"/> (docs/adr/ADR-056).</summary>
    public const ushort CastSkill = 0xC204;

    /// <summary>Asks for the player's kill counts per species and the equipped slayer title (docs/adr/ADR-068). Reply: <see cref="S2C.BestiarySnapshot"/>.</summary>
    public const ushort GetBestiary = 0xC205;

    /// <summary>Equips or takes off a slayer title (docs/adr/ADR-068). Reply: <see cref="S2C.EquipBestiaryTitleResult"/>.</summary>
    public const ushort EquipBestiaryTitle = 0xC206;

    // -- S2C (0xC240-0xC27F) --------------------------------------------------

    /// <summary>Current player vitals (HP, MaxHp, MP, MaxMp, CombatXp).</summary>
    public const ushort VitalsState = 0xC240;

    /// <summary>Every live monster on the player's current map or floor.</summary>
    public const ushort MonstersSnapshot = 0xC241;

    /// <summary>Push when a monster takes damage or is defeated on the map.</summary>
    public const ushort MonsterChanged = 0xC242;

    /// <summary>Reply to <see cref="AttackMonster"/>.</summary>
    public const ushort AttackMonsterResult = 0xC243;

    /// <summary>Reply to <see cref="DrinkPotion"/>.</summary>
    public const ushort DrinkPotionResult = 0xC244;

    /// <summary>Reply to <see cref="CastSkill"/> (docs/adr/ADR-056).</summary>
    public const ushort CastSkillResult = 0xC245;

    /// <summary>Push when a boss telegraphs a ground slam on a tile; the impact follows after <c>DelayMs</c> (docs/adr/ADR-047).</summary>
    public const ushort GroundTelegraph = 0xC246;

    /// <summary>Reply to <see cref="GetBestiary"/> (docs/adr/ADR-068).</summary>
    public const ushort BestiarySnapshot = 0xC247;

    /// <summary>Reply to <see cref="EquipBestiaryTitle"/> (docs/adr/ADR-068).</summary>
    public const ushort EquipBestiaryTitleResult = 0xC248;

    /// <summary>Push when an ordinary monster winds up a melee strike at a player; it lands after <c>DelayMs</c> if the player is still in reach (docs/adr/ADR-092).</summary>
    public const ushort StrikeTelegraph = 0xC249;
}
