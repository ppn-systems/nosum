// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Skills;
using Nosum.Gameplay.Weather;

namespace Nosum.Gameplay.Combat;

/// <summary>
/// Pure rules for player HP &amp; MP vitals, weapon/staff combat damage, potion restoration, elite modifiers, and monster encounters
/// (docs/adr/ADR-045 D2, D3, ADR-058, ADR-060).
/// </summary>
public static class CombatRules
{
    /// <summary>Base maximum HP at Combat level 0.</summary>
    public const int BaseMaxHp = 100;

    /// <summary>Additional maximum HP per Combat level.</summary>
    public const int HpPerLevel = 10;

    /// <summary>Base maximum MP at Combat level 0.</summary>
    public const int BaseMaxMp = 50;

    /// <summary>Additional maximum MP per Combat level.</summary>
    public const int MpPerLevel = 5;

    /// <summary>Highest possible player MaxHp (at Combat level 10).</summary>
    public const int MaxHpCap = BaseMaxHp + (SkillRules.MaxLevel * HpPerLevel);

    /// <summary>Highest possible player MaxMp (at Combat level 10).</summary>
    public const int MaxMpCap = BaseMaxMp + (SkillRules.MaxLevel * MpPerLevel);

    /// <summary>MP consumed when casting a bolt with <see cref="ItemCatalog.ArcaneStaff"/>.</summary>
    public const int StaffMpCost = 10;

    /// <summary>Maximum tile distance (Chebyshev) for a melee sword strike.</summary>
    public const int MeleeRange = 2;

    /// <summary>Maximum tile distance (Chebyshev) at which a monster can strike back: its contact reach (docs/adr/ADR-046 D1).</summary>
    public const int MonsterMeleeRange = 1;

    /// <summary>Maximum tile distance (Chebyshev) for an arcane staff bolt.</summary>
    public const int StaffRange = 4;

    /// <summary>Maximum number of monsters on a single map or floor.</summary>
    public const int MaxMonstersPerMap = 16;

    /// <summary>Maximum tile distance for party members to receive shared Combat XP from monster defeats (docs/adr/ADR-052).</summary>
    public const int CoopShareRadius = 12;

    /// <summary>Aggro detection radius in tiles (Chebyshev) for monsters (docs/adr/ADR-046 D1).</summary>
    public const int AggroRadius = 4;

    /// <summary>Boss aggro detection radius in tiles (Chebyshev) for boss encounters (docs/adr/ADR-047).</summary>
    public const int BossAggroRadius = 6;

    /// <summary>Experience multiplier awarded for defeating an elite monster with a non-none <see cref="MonsterModifier"/> (docs/adr/ADR-058).</summary>
    public const double EliteXpMultiplier = 2.5;

    /// <summary>Damage multiplier applied when striking a <see cref="MonsterModifier.Shielded"/> monster (docs/adr/ADR-058).</summary>
    public const double ShieldedDamageMultiplier = 0.75;

    /// <summary>Reflected fire damage dealt to the attacker when striking a <see cref="MonsterModifier.FlameAura"/> monster in melee (docs/adr/ADR-058).</summary>
    public const int FlameAuraReflectDamage = 3;

    /// <summary>Cooldown in milliseconds per player per sanctuary shrine blessing (docs/adr/ADR-063).</summary>
    public const int ShrineCooldownMs = ShrineRules.ShrineCooldownMs;

    /// <summary>Radius in tiles (Chebyshev) struck by a boss ground slam around its telegraphed tile.</summary>
    public const int GroundSlamRadiusTiles = 1;

    /// <summary>Warning time in milliseconds between a ground slam telegraph and its impact (the dodge window).</summary>
    public const int GroundSlamTelegraphMs = 1000;

    /// <summary>Cooldown in milliseconds between one boss ground slam and the next from the same boss.</summary>
    public const int GroundSlamCooldownMs = 6000;

    /// <summary>Maximum duration in milliseconds to maintain a combat combo streak.</summary>
    public const int ComboTimeoutMs = 3500;

    /// <summary>Maximum combo streak multiplier.</summary>
    public const int MaxComboCount = 10;

    /// <summary>Returns whether a monster strikes back when hit from <paramref name="distanceTiles"/> away (issue #147).</summary>
    /// <param name="distanceTiles">Chebyshev tile distance between the attacker and the monster.</param>
    /// <returns><see langword="true"/> when the attacker stands within the monster's contact reach.</returns>
    public static bool MonsterStrikesBack(int distanceTiles) => distanceTiles <= MonsterMeleeRange;

    /// <summary>Calculates HP healed by a <see cref="MonsterModifier.Vampiric"/> monster when it deals <paramref name="damage"/> to a player.</summary>
    /// <param name="damage">Damage dealt to the player.</param>
    /// <returns>HP restored to the monster (minimum 1).</returns>
    public static int CalculateVampiricHeal(int damage) => Math.Max(1, damage / 2);

    /// <summary>Calculates reduced damage dealt to a <see cref="MonsterModifier.Shielded"/> monster.</summary>
    /// <param name="rawDamage">Incoming player attack damage.</param>
    /// <returns>Mitigated damage (minimum 1).</returns>
    public static int CalculateShieldedDamage(int rawDamage)
        => Math.Max(1, (int)MathF.Round(rawDamage * (float)ShieldedDamageMultiplier));

    /// <summary>Calculates total XP awarded when defeating an elite monster.</summary>
    /// <param name="baseXp">Base XP reward of the species.</param>
    /// <returns>Scaled elite XP reward.</returns>
    public static int CalculateEliteXp(int baseXp) => (int)Math.Round(baseXp * EliteXpMultiplier);

    /// <summary>Calculates shared Combat XP awarded to nearby party/co-op allies.</summary>
    /// <param name="baseXp">XP awarded to the killer.</param>
    /// <returns>Shared ally XP (minimum 1).</returns>
    public static int CalculatePartySharedXp(int baseXp) => Math.Max(1, baseXp / 2);

    /// <summary>Returns whether a monster species is a dungeon floor boss (docs/adr/ADR-047).</summary>
    /// <param name="kind">Monster species.</param>
    /// <returns><see langword="true"/> when the monster is a boss.</returns>
    public static bool IsBoss(MonsterKind kind) => MonsterSpawnPlanner.IsBoss(kind);

    /// <summary>Returns whether a mine floor is a milestone boss floor (10, 20, 30, 40).</summary>
    /// <param name="floor">Mine floor (1..40).</param>
    /// <returns><see langword="true"/> when a floor holds a boss encounter.</returns>
    public static bool IsBossFloor(int floor) => MonsterSpawnPlanner.IsBossFloor(floor);

    /// <summary>Returns the designated boss species for a milestone mine floor.</summary>
    /// <param name="floor">Mine floor (1..40).</param>
    /// <returns>The boss species or <see cref="MonsterKind.None"/>.</returns>
    public static MonsterKind BossForFloor(int floor) => MonsterSpawnPlanner.BossForFloor(floor);

    /// <summary>Contact damage dealt by a monster species when it attacks a player (docs/adr/ADR-046 D1).</summary>
    /// <param name="kind">Monster species.</param>
    /// <returns>Contact damage.</returns>
    public static int MonsterDamage(MonsterKind kind)
        => MonsterCatalog.TryGet(kind, out MonsterDefinition? def) ? def.AttackDamage : 5;

    /// <summary>
    /// Whether a monster is enraged: alive, its species enrages, and its HP is at or below the species threshold (docs/adr/ADR-047).
    /// </summary>
    /// <param name="monster">The monster state to check.</param>
    /// <returns><see langword="true"/> when the monster is enraged.</returns>
    public static bool IsEnraged(MonsterSpawn monster)
        => monster.Hp > 0
            && MonsterCatalog.TryGet(monster.Kind, out MonsterDefinition? def)
            && def.EnrageThresholdPercent > 0
            && monster.Hp * 100 <= monster.MaxHp * def.EnrageThresholdPercent;

    /// <summary>Damage dealt by a boss ground slam to each player inside its radius (heavy attack, twice the contact damage).</summary>
    /// <param name="kind">Monster species.</param>
    /// <returns>Slam damage before defense mitigation.</returns>
    public static int GroundSlamDamage(MonsterKind kind) => MonsterDamage(kind) * 2;

    /// <summary>Calculates defense provided by worn armor and helmet items (docs/adr/ADR-049).</summary>
    /// <param name="hatItemId">Worn hat/crown item id.</param>
    /// <param name="shirtItemId">Worn armor/chestplate item id.</param>
    /// <param name="pantsItemId">Worn greaves/pants item id.</param>
    /// <returns>Total damage reduction defense.</returns>
    public static int CalculateDefense(int hatItemId, int shirtItemId, int pantsItemId)
        => ArmorDefense(hatItemId) + ArmorDefense(shirtItemId) + ArmorDefense(pantsItemId);

    /// <summary>Returns the defense rating of an equipment piece.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>Defense rating.</returns>
    public static int ArmorDefense(int itemId) => itemId switch
    {
        ItemCatalog.BossCrown => 6,
        ItemCatalog.IronChestplate => 5,
        ItemCatalog.IronGreaves => 3,
        ItemCatalog.IronHelmet => 2,
        ItemCatalog.RangerTunic => 3,
        ItemCatalog.LeatherArmor => 2,
        _ => 0,
    };

    /// <summary>Mitigates raw incoming attack damage using player defense.</summary>
    /// <param name="rawDamage">Raw incoming monster attack damage.</param>
    /// <param name="defense">Player defense rating.</param>
    /// <returns>Mitigated damage (minimum 1).</returns>
    public static int MitigateDamage(int rawDamage, int defense)
        => Math.Max(1, rawDamage - defense);

    /// <summary>Calculates maximum HP for a given Combat skill level.</summary>
    /// <param name="combatLevel">Combat level (0..10).</param>
    /// <returns>Maximum HP.</returns>
    public static int MaxHpForLevel(int combatLevel)
        => BaseMaxHp + (Math.Clamp(combatLevel, 0, SkillRules.MaxLevel) * HpPerLevel);

    /// <summary>Calculates maximum MP for a given Combat skill level.</summary>
    /// <param name="combatLevel">Combat level (0..10).</param>
    /// <returns>Maximum MP.</returns>
    public static int MaxMpForLevel(int combatLevel)
        => BaseMaxMp + (Math.Clamp(combatLevel, 0, SkillRules.MaxLevel) * MpPerLevel);

    /// <summary>Calculates the HP restored when a player is knocked out and wakes up at the farmhouse (25% of maximum HP, minimum 1).</summary>
    /// <param name="maxHp">Player's maximum HP.</param>
    /// <returns>Revived HP amount.</returns>
    public static int ReviveHp(int maxHp)
        => Math.Max(1, maxHp / 4);

    /// <summary>Returns the base weapon damage of an item, or 0 if it is not a combat weapon.</summary>
    /// <param name="itemId">The held item id.</param>
    /// <returns>Base damage.</returns>
    public static int WeaponDamage(int itemId) => itemId switch
    {
        ItemCatalog.WoodenSword => 12,
        ItemCatalog.BronzeSword => 20,
        ItemCatalog.IronSword => 32,
        ItemCatalog.SteelSword => 48,
        ItemCatalog.GoldSword => 68,
        ItemCatalog.ArcaneStaff => 45,
        _ => 0,
    };

    /// <summary>Returns the MP cost required to attack with an item (0 for melee swords, <see cref="StaffMpCost"/> for staves).</summary>
    /// <param name="itemId">The held item id.</param>
    /// <returns>MP cost.</returns>
    public static int AttackMpCost(int itemId) => itemId == ItemCatalog.ArcaneStaff ? StaffMpCost : 0;

    /// <summary>Returns the maximum attack range in tiles (Chebyshev distance) for a weapon.</summary>
    /// <param name="itemId">The held item id.</param>
    /// <returns>Range in tiles, or 0 if not a weapon.</returns>
    public static int AttackRange(int itemId) => itemId switch
    {
        ItemCatalog.ArcaneStaff => StaffRange,
        ItemCatalog.WoodenSword or ItemCatalog.BronzeSword or ItemCatalog.IronSword or ItemCatalog.SteelSword or ItemCatalog.GoldSword => MeleeRange,
        _ => 0,
    };

    /// <summary>Calculates total damage dealt by a player attack, including Combat level bonus.</summary>
    /// <param name="itemId">Held weapon item id.</param>
    /// <param name="combatLevel">Player's Combat skill level (0..10).</param>
    /// <returns>Total damage dealt.</returns>
    public static int CalculateDamage(int itemId, int combatLevel)
    {
        int baseDamage = WeaponDamage(itemId);
        if (baseDamage <= 0)
        {
            return 0;
        }

        return baseDamage + (Math.Clamp(combatLevel, 0, SkillRules.MaxLevel) * 2);
    }

    /// <summary>Calculates bonus damage percent for a given combo count (e.g. 5 combo = +20% bonus).</summary>
    /// <param name="comboCount">Active combo streak.</param>
    /// <returns>Multiplier bonus fraction (0.0 to 0.45).</returns>
    public static float ComboBonusPercent(int comboCount) => Math.Clamp(Math.Max(0, comboCount - 1) * 0.05f, 0f, 0.45f);

    /// <summary>Calculates total damage dealt by a player attack, including Combat level bonus and combo streak (docs/adr/ADR-060).</summary>
    /// <param name="itemId">Held weapon item id.</param>
    /// <param name="combatLevel">Player's Combat skill level (0..10).</param>
    /// <param name="comboCount">Active combo streak (1..10).</param>
    /// <returns>Total damage dealt.</returns>
    public static int CalculateDamage(int itemId, int combatLevel, int comboCount)
    {
        int baseDmg = CalculateDamage(itemId, combatLevel);
        if (baseDmg <= 0 || comboCount <= 1)
        {
            return baseDmg;
        }

        return ApplyComboMultiplier(baseDmg, comboCount);
    }

    /// <summary>Scales any player hit (weapon swing or skill) by the combo streak bonus (docs/adr/ADR-060).</summary>
    /// <param name="baseDamage">Damage before the combo bonus.</param>
    /// <param name="comboCount">Active combo streak (1..10).</param>
    /// <returns>Damage with the combo bonus applied (minimum 1 when the base is positive).</returns>
    public static int ApplyComboMultiplier(int baseDamage, int comboCount)
    {
        if (baseDamage <= 0 || comboCount <= 1)
        {
            return baseDamage;
        }

        float bonus = ComboBonusPercent(comboCount);
        return Math.Max(1, (int)MathF.Round(baseDamage * (1f + bonus)));
    }

    /// <summary>Returns the HP and MP restored when consuming a potion or food item.</summary>
    /// <param name="itemId">The food/potion item id.</param>
    /// <returns>A tuple of <c>(HpRestore, MpRestore)</c>.</returns>
    public static (int Hp, int Mp) PotionRestore(int itemId) => itemId switch
    {
        ItemCatalog.HealthPotion => (50, 0),
        ItemCatalog.ManaPotion => (0, 35),
        ItemCatalog.Elixir => (100, 50),
        ItemCatalog.ArcaneGrimoire => (20, 80),
        ItemCatalog.HeartCrystal => (80, 25),
        ItemCatalog.Salad => (20, 5),
        ItemCatalog.BakedPotato => (15, 0),
        ItemCatalog.PumpkinSoup => (35, 15),
        _ => (0, 0),
    };

    /// <summary>Returns whether an item can be consumed as a combat potion via <c>DrinkPotion</c>.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns><see langword="true"/> when the item restores HP or MP.</returns>
    public static bool IsPotion(int itemId)
    {
        (int hp, int mp) = PotionRestore(itemId);
        return hp > 0 || mp > 0;
    }

    /// <summary>Returns whether a player with current vitals <paramref name="hp"/>/<paramref name="maxHp"/> and <paramref name="mp"/>/<paramref name="maxMp"/> may consume <paramref name="itemId"/> now.</summary>
    /// <param name="itemId">The item id to consume.</param>
    /// <param name="hp">Current player HP.</param>
    /// <param name="maxHp">Maximum player HP.</param>
    /// <param name="mp">Current player MP.</param>
    /// <param name="maxMp">Maximum player MP.</param>
    /// <returns><see langword="true"/> when the item is a valid potion/food and provides restoration or a status buff.</returns>
    public static bool CanConsumePotion(int itemId, int hp, int maxHp, int mp, int maxMp)
    {
        if (!ItemCatalog.TryGet(itemId, out ItemDefinition? item) || item.Kind != ItemKind.Food || !IsPotion(itemId))
        {
            return false;
        }

        (int healHp, int restoreMp) = PotionRestore(itemId);
        (StatusEffectKind effect, _, _) = PotionEffect(itemId);
        return itemId is ItemCatalog.ArcaneGrimoire or ItemCatalog.HeartCrystal
            || effect != StatusEffectKind.None
            || (healHp > 0 && hp < maxHp)
            || (restoreMp > 0 && mp < maxMp);
    }

    /// <summary>Returns the status effect granted by a potion or elixir (docs/adr/ADR-051, ADR-065).</summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>Status effect kind, duration in seconds, and magnitude per tick.</returns>
    public static (StatusEffectKind Effect, int DurationSeconds, int TickMagnitude) PotionEffect(int itemId) => itemId switch
    {
        ItemCatalog.Elixir => (StatusEffectKind.Regeneration, 30, 5),
        ItemCatalog.ArcaneGrimoire => (StatusEffectKind.Swiftness, 30, 1),
        ItemCatalog.HeartCrystal => (StatusEffectKind.Regeneration, 40, 8),
        ItemCatalog.PumpkinSoup => (StatusEffectKind.Swiftness, 20, 1),
        _ => (StatusEffectKind.None, 0, 0),
    };

    /// <summary>Returns the debuff status effect inflicted by monster contact attacks (docs/adr/ADR-051).</summary>
    /// <param name="kind">Monster species.</param>
    /// <returns>Status effect kind, duration in seconds, and damage per tick.</returns>
    public static (StatusEffectKind Effect, int DurationSeconds, int TickMagnitude) MonsterStatusOnHit(MonsterKind kind) => kind switch
    {
        MonsterKind.CaveBat => (StatusEffectKind.Poison, 6, 2),
        MonsterKind.SporeShroom => (StatusEffectKind.Poison, 10, 3),
        MonsterKind.BossBroodmother => (StatusEffectKind.Poison, 12, 4),
        _ => (StatusEffectKind.None, 0, 0),
    };

    /// <summary>Selects a monster species for a given mine floor and spawn index.</summary>
    /// <param name="floor">Mine floor (1..40).</param>
    /// <param name="index">Spawn slot index.</param>
    /// <returns>The monster species.</returns>
    public static MonsterKind SpeciesForMineFloor(int floor, int index)
        => MonsterSpawnPlanner.SpeciesForMineFloor(floor, index);

    /// <summary>Plans deterministic monster spawns for a mine floor or wilderness map.</summary>
    /// <param name="mapId">The map id (<c>mine1</c>..<c>mine40</c>, <c>Woods</c>, <c>Mountain</c>, <c>Backwoods</c>).</param>
    /// <param name="seed">Deterministic seed.</param>
    /// <param name="grid">The map's walk grid.</param>
    /// <param name="occupied">Tiles already occupied by nodes or arrival points.</param>
    /// <param name="spawnX">Player arrival X to keep clear.</param>
    /// <param name="spawnY">Player arrival Y to keep clear.</param>
    /// <param name="reachableMask">Optional precomputed reachability mask.</param>
    /// <param name="weather">The weather of the instance's game day (docs/adr/ADR-058).</param>
    /// <returns>The spawned monsters.</returns>
    public static List<MonsterSpawn> PlanMonsters(
        string mapId,
        ulong seed,
        WalkGrid grid,
        IReadOnlySet<(int X, int Y)> occupied,
        int spawnX,
        int spawnY,
        bool[,]? reachableMask = null,
        WeatherKind weather = WeatherKind.Sun)
        => MonsterSpawnPlanner.PlanMonsters(mapId, seed, grid, occupied, spawnX, spawnY, reachableMask, weather);

    /// <summary>Determines whether a rhythmic <see cref="DungeonHazardKind.SpikeTrap"/> is extended and dealing damage at <paramref name="nowMs"/>.</summary>
    /// <param name="hazardId">Hazard id within the floor instance.</param>
    /// <param name="nowMs">Server or client timestamp in Unix milliseconds.</param>
    /// <returns><see langword="true"/> when the spikes are extended.</returns>
    public static bool IsSpikeTrapActive(int hazardId, long nowMs)
        => DungeonHazardRules.IsSpikeTrapActive(hazardId, nowMs);

    /// <summary>Plans deterministic environmental hazards for a dungeon floor or map (docs/adr/ADR-061).</summary>
    /// <param name="mapId">Map or floor identifier.</param>
    /// <param name="seed">Deterministic world seed.</param>
    /// <param name="grid">Collision grid of the map.</param>
    /// <param name="spawnX">Player arrival tile X.</param>
    /// <param name="spawnY">Player arrival tile Y.</param>
    /// <param name="occupied">Optional set of tiles already occupied by resource nodes or monsters.</param>
    /// <param name="reachableMask">Optional precomputed reachability mask.</param>
    /// <returns>A list of planned hazards.</returns>
    public static List<DungeonHazard> PlanHazards(
        string mapId,
        ulong seed,
        WalkGrid grid,
        int spawnX,
        int spawnY,
        IReadOnlySet<(int X, int Y)>? occupied = null,
        bool[,]? reachableMask = null)
        => DungeonHazardRules.PlanHazards(mapId, seed, grid, spawnX, spawnY, occupied, reachableMask);

    /// <summary>Returns <see langword="true"/> when <paramref name="kind"/> is a beneficial dungeon blessing shrine or elemental resonance pylon (docs/adr/ADR-063, ADR-064).</summary>
    /// <param name="kind">Hazard or shrine kind.</param>
    /// <returns><see langword="true"/> for sanctuary shrines and resonance pylons; <see langword="false"/> for damaging traps.</returns>
    public static bool IsShrine(DungeonHazardKind kind) => ShrineRules.IsShrine(kind);

    /// <summary>Returns <see langword="true"/> when <paramref name="kind"/> is an elemental resonance pylon that also discharges damage against nearby monsters (docs/adr/ADR-064).</summary>
    /// <param name="kind">Hazard or shrine kind.</param>
    /// <returns><see langword="true"/> for elemental resonance pylons and wayshrines.</returns>
    public static bool IsResonancePylon(DungeonHazardKind kind) => ShrineRules.IsResonancePylon(kind);

    /// <summary>Returns the elemental discharge parameters against nearby monsters when a resonance pylon is triggered (docs/adr/ADR-064).</summary>
    /// <param name="kind">Pylon kind.</param>
    /// <returns>Tuple of <c>(MonsterDamage, RadiusTiles, SkillKind)</c>.</returns>
    public static (int MonsterDamage, int RadiusTiles, CombatSkillKind SkillKind) PylonDischarge(DungeonHazardKind kind)
        => ShrineRules.PylonDischarge(kind);

    /// <summary>Returns the blessing rewards granted by a sanctuary shrine or resonance pylon (docs/adr/ADR-063, ADR-064).</summary>
    /// <param name="kind">Shrine kind.</param>
    /// <returns>Tuple of <c>(HealHp, RestoreMp, BonusXp, Buff)</c>.</returns>
    public static (int HealHp, int RestoreMp, int BonusXp, StatusEffectKind Buff) ShrineBlessing(DungeonHazardKind kind)
        => ShrineRules.ShrineBlessing(kind);

    /// <summary>Deterministically plans beneficial Dungeon Blessing Shrines, Overworld Wayshrines, and Elemental Resonance Pylons on a combat map (docs/adr/ADR-063, ADR-064).</summary>
    /// <param name="mapId">Map id (e.g., <c>mine10</c>, <c>forest</c>, <c>mountain</c>).</param>
    /// <param name="seed">Deterministic instance seed.</param>
    /// <param name="grid">Map walkability grid.</param>
    /// <param name="spawnX">Player arrival tile X.</param>
    /// <param name="spawnY">Player arrival tile Y.</param>
    /// <param name="occupied">Optional set of already-occupied tiles to avoid.</param>
    /// <param name="reachableMask">Optional precomputed reachability mask.</param>
    /// <returns>Planned shrines and resonance pylons for the map.</returns>
    public static List<DungeonHazard> PlanShrines(
        string mapId,
        ulong seed,
        WalkGrid grid,
        int spawnX,
        int spawnY,
        IReadOnlySet<(int X, int Y)>? occupied = null,
        bool[,]? reachableMask = null)
        => ShrineRules.PlanShrines(mapId, seed, grid, spawnX, spawnY, occupied, reachableMask);
}
