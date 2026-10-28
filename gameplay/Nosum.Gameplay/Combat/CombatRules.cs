// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Skills;

namespace Nosum.Gameplay.Combat;

/// <summary>A spawned monster on a map or mine floor.</summary>
/// <param name="MonsterId">Stable id within the map/floor instance (1..N).</param>
/// <param name="Kind">The monster species.</param>
/// <param name="X">Tile X.</param>
/// <param name="Y">Tile Y.</param>
/// <param name="Hp">Current HP.</param>
/// <param name="MaxHp">Maximum HP.</param>
/// <param name="Modifier">Elite affix modifier (docs/adr/ADR-058).</param>
public readonly record struct MonsterSpawn(int MonsterId, MonsterKind Kind, int X, int Y, int Hp, int MaxHp, MonsterModifier Modifier = MonsterModifier.None);

/// <summary>
/// Pure rules for player HP &amp; MP vitals, weapon/staff combat damage, potion restoration, and deterministic monster spawning
/// (docs/adr/ADR-045 D2, D3).
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

    /// <summary>Returns whether a monster species is a dungeon floor boss (docs/adr/ADR-047).</summary>
    /// <param name="kind">Monster species.</param>
    /// <returns><see langword="true"/> when the monster is a boss.</returns>
    public static bool IsBoss(MonsterKind kind) => kind is MonsterKind.BossBroodmother
        or MonsterKind.BossGoblinChieftain
        or MonsterKind.BossMagmaColossus
        or MonsterKind.BossGolemKing;

    /// <summary>Returns whether a mine floor is a milestone boss floor (10, 20, 30, 40).</summary>
    /// <param name="floor">Mine floor (1..40).</param>
    /// <returns><see langword="true"/> when a floor holds a boss encounter.</returns>
    public static bool IsBossFloor(int floor) => floor is 10 or 20 or 30 or 40;

    /// <summary>Returns the designated boss species for a milestone mine floor.</summary>
    /// <param name="floor">Mine floor (1..40).</param>
    /// <returns>The boss species or <see cref="MonsterKind.None"/>.</returns>
    public static MonsterKind BossForFloor(int floor) => floor switch
    {
        10 => MonsterKind.BossBroodmother,
        20 => MonsterKind.BossGoblinChieftain,
        30 => MonsterKind.BossMagmaColossus,
        40 => MonsterKind.BossGolemKing,
        _ => MonsterKind.None,
    };

    /// <summary>Contact damage dealt by a monster species when it attacks a player (docs/adr/ADR-046 D1).</summary>
    /// <param name="kind">Monster species.</param>
    /// <returns>Contact damage.</returns>
    public static int MonsterDamage(MonsterKind kind) => kind switch
    {
        MonsterKind.GreenSlime => 4,
        MonsterKind.CaveBat => 6,
        MonsterKind.SporeShroom => 5,
        MonsterKind.FrostSlime => 8,
        MonsterKind.GoblinScout => 10,
        MonsterKind.SkeletonWarrior => 14,
        MonsterKind.MagmaSlime => 15,
        MonsterKind.StoneGolem => 20,
        MonsterKind.BossBroodmother => 15,
        MonsterKind.BossGoblinChieftain => 22,
        MonsterKind.BossMagmaColossus => 30,
        MonsterKind.BossGolemKing => 40,
        _ => 5,
    };

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
        ItemCatalog.BossCrown => 8,
        ItemCatalog.IronChestplate => 7,
        ItemCatalog.IronGreaves => 5,
        ItemCatalog.IronHelmet => 4,
        ItemCatalog.LeatherArmor => 3,
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

    /// <summary>Maximum duration in milliseconds to maintain a combat combo streak.</summary>
    public const int ComboTimeoutMs = 3500;

    /// <summary>Maximum combo streak multiplier.</summary>
    public const int MaxComboCount = 10;

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

        float bonus = ComboBonusPercent(comboCount);
        return Math.Max(1, (int)MathF.Round(baseDmg * (1f + bonus)));
    }

    /// <summary>Returns the HP and MP restored when consuming a potion or food item.</summary>
    /// <param name="itemId">The food/potion item id.</param>
    /// <returns>A tuple of <c>(HpRestore, MpRestore)</c>.</returns>
    public static (int Hp, int Mp) PotionRestore(int itemId) => itemId switch
    {
        ItemCatalog.HealthPotion => (50, 0),
        ItemCatalog.ManaPotion => (0, 35),
        ItemCatalog.Elixir => (100, 50),
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

    /// <summary>Returns the status effect granted by a potion or elixir (docs/adr/ADR-051).</summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>Status effect kind, duration in seconds, and magnitude per tick.</returns>
    public static (StatusEffectKind Effect, int DurationSeconds, int TickMagnitude) PotionEffect(int itemId) => itemId switch
    {
        ItemCatalog.Elixir => (StatusEffectKind.Regeneration, 30, 5),
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
    {
        if (index == 0 && IsBossFloor(floor))
        {
            return BossForFloor(floor);
        }

        if (floor <= 10)
        {
            return (index % 3) switch
            {
                0 => MonsterKind.GreenSlime,
                1 => MonsterKind.CaveBat,
                _ => MonsterKind.SporeShroom,
            };
        }

        if (floor <= 25)
        {
            return (index % 3) switch
            {
                0 => MonsterKind.FrostSlime,
                1 => MonsterKind.GoblinScout,
                _ => MonsterKind.SporeShroom,
            };
        }

        return (index % 3) switch
        {
            0 => MonsterKind.MagmaSlime,
            1 => MonsterKind.SkeletonWarrior,
            _ => MonsterKind.StoneGolem,
        };
    }

    /// <summary>Plans deterministic monster spawns for a mine floor or wilderness map.</summary>
    /// <param name="mapId">The map id (<c>mine1</c>..<c>mine40</c>, <c>Woods</c>, <c>Mountain</c>, <c>Backwoods</c>).</param>
    /// <param name="seed">Deterministic seed.</param>
    /// <param name="grid">The map's walk grid.</param>
    /// <param name="occupied">Tiles already occupied by nodes or arrival points.</param>
    /// <param name="spawnX">Player arrival X to keep clear.</param>
    /// <param name="spawnY">Player arrival Y to keep clear.</param>
    /// <returns>The spawned monsters.</returns>
    public static List<MonsterSpawn> PlanMonsters(
        string mapId,
        ulong seed,
        WalkGrid grid,
        IReadOnlySet<(int X, int Y)> occupied,
        int spawnX,
        int spawnY)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(occupied);

        int floor = 0;
        bool isMine = mapId.StartsWith("mine", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(mapId.AsSpan(4), out floor)
            && floor >= 1
            && floor <= MineRules.MaxFloor;
        bool isWoods = string.Equals(mapId, GameMaps.Woods, StringComparison.OrdinalIgnoreCase) || string.Equals(mapId, GameMaps.Forest, StringComparison.OrdinalIgnoreCase);
        bool isMountain = string.Equals(mapId, GameMaps.Mountain, StringComparison.OrdinalIgnoreCase) || string.Equals(mapId, GameMaps.Backwoods, StringComparison.OrdinalIgnoreCase);

        if (!isMine && !isWoods && !isMountain)
        {
            return [];
        }

        List<(int X, int Y)> nearCandidates = [];
        List<(int X, int Y)> allCandidates = [];
        for (int y = 2; y < grid.Height - 2; y++)
        {
            for (int x = 2; x < grid.Width - 2; x++)
            {
                int dist = Math.Max(Math.Abs(x - spawnX), Math.Abs(y - spawnY));
                if (!grid.IsBlocked(x, y)
                    && !occupied.Contains((x, y))
                    && dist >= 3)
                {
                    if (isMine && dist <= 7)
                    {
                        nearCandidates.Add((x, y));
                    }

                    allCandidates.Add((x, y));
                }
            }
        }

        List<(int X, int Y)> candidates = nearCandidates.Count >= 4 ? nearCandidates : allCandidates;
        if (candidates.Count == 0)
        {
            return [];
        }

        NodeRandom random = new(NodeRandom.Mix(seed, 0x4D4F4E53, floor));
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int count = isMine
            ? Math.Clamp(3 + (floor / 8), 3, 8)
            : 5;

        List<MonsterSpawn> spawns = new(Math.Min(count, candidates.Count));
        for (int i = 0; i < count && i < candidates.Count; i++)
        {
            MonsterKind kind = isMine
                ? SpeciesForMineFloor(floor, i)
                : isWoods
                    ? (i % 2 == 0 ? MonsterKind.GreenSlime : MonsterKind.SporeShroom)
                    : (i % 2 == 0 ? MonsterKind.GoblinScout : MonsterKind.FrostSlime);

            if (!MonsterCatalog.TryGet(kind, out MonsterDefinition? def))
            {
                continue;
            }

            MonsterModifier modifier = MonsterModifier.None;
            if (!IsBoss(kind) && random.Next(100) < 35)
            {
                modifier = (MonsterModifier)(1 + random.Next(4));
            }

            int maxHp = def.MaxHp;
            if (modifier == MonsterModifier.Shielded)
            {
                maxHp = (int)(maxHp * 1.5);
            }
            else if (modifier == MonsterModifier.FlameAura)
            {
                maxHp = (int)(maxHp * 1.25);
            }
            else if (modifier == MonsterModifier.Vampiric)
            {
                maxHp = (int)(maxHp * 1.2);
            }

            (int x, int y) = candidates[i];
            spawns.Add(new MonsterSpawn(i + 1, kind, x, y, maxHp, maxHp, modifier));
        }

        return spawns;
    }

    /// <summary>
    /// Plans deterministic environmental hazards for a dungeon floor or map (docs/adr/ADR-061).
    /// </summary>
    /// <param name="mapId">Map or floor identifier.</param>
    /// <param name="seed">Deterministic world seed.</param>
    /// <param name="grid">Collision grid of the map.</param>
    /// <param name="spawnX">Player arrival tile X.</param>
    /// <param name="spawnY">Player arrival tile Y.</param>
    /// <param name="occupied">Optional set of tiles already occupied by resource nodes or monsters.</param>
    /// <returns>A list of planned hazards.</returns>
    public static List<DungeonHazard> PlanHazards(
        string mapId,
        ulong seed,
        WalkGrid grid,
        int spawnX,
        int spawnY,
        IReadOnlySet<(int X, int Y)>? occupied = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentNullException.ThrowIfNull(grid);

        int floor = 0;
        bool isMine = mapId.StartsWith("mine", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(mapId.AsSpan(4), out floor)
            && floor >= 1
            && floor <= MineRules.MaxFloor;

        if (!isMine)
        {
            return [];
        }

        List<(int X, int Y)> nearCandidates = [];
        List<(int X, int Y)> allCandidates = [];
        for (int y = 2; y < grid.Height - 2; y++)
        {
            for (int x = 2; x < grid.Width - 2; x++)
            {
                int dist = Math.Max(Math.Abs(x - spawnX), Math.Abs(y - spawnY));
                if (!grid.IsBlocked(x, y)
                    && (occupied is null || !occupied.Contains((x, y)))
                    && dist >= 2)
                {
                    if (dist <= 6)
                    {
                        nearCandidates.Add((x, y));
                    }

                    allCandidates.Add((x, y));
                }
            }
        }

        List<(int X, int Y)> candidates = nearCandidates.Count >= 2 ? nearCandidates : allCandidates;
        if (candidates.Count == 0)
        {
            return [];
        }

        NodeRandom random = new(NodeRandom.Mix(seed, 0x48415A44, floor));
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int count = Math.Clamp(2 + (floor / 10), 2, 6);
        DungeonHazardKind kind = floor switch
        {
            <= 10 => DungeonHazardKind.SporeVent,
            <= 20 => DungeonHazardKind.SpikeTrap,
            _ => DungeonHazardKind.MagmaFissure,
        };

        byte damage = kind switch
        {
            DungeonHazardKind.SporeVent => 4,
            DungeonHazardKind.SpikeTrap => 8,
            DungeonHazardKind.MagmaFissure => 14,
            _ => 0,
        };

        List<DungeonHazard> hazards = new(Math.Min(count, candidates.Count));
        for (int i = 0; i < count && i < candidates.Count; i++)
        {
            (int x, int y) = candidates[i];
            hazards.Add(new DungeonHazard((byte)(i + 1), kind, (short)x, (short)y, damage));
        }

        return hazards;
    }

    /// <summary>Cooldown in milliseconds per player per sanctuary shrine blessing (docs/adr/ADR-063).</summary>
    public const int ShrineCooldownMs = 12000;

    /// <summary>Returns <see langword="true"/> when <paramref name="kind"/> is a beneficial dungeon blessing shrine or elemental resonance pylon (docs/adr/ADR-063, ADR-064).</summary>
    /// <param name="kind">Hazard or shrine kind.</param>
    /// <returns><see langword="true"/> for sanctuary shrines and resonance pylons; <see langword="false"/> for damaging traps.</returns>
    public static bool IsShrine(DungeonHazardKind kind) => kind switch
    {
        DungeonHazardKind.VitalityShrine or
        DungeonHazardKind.ArcaneObelisk or
        DungeonHazardKind.EmpowermentAltar or
        DungeonHazardKind.ThunderPylon or
        DungeonHazardKind.SolarWayshrine or
        DungeonHazardKind.FrostMonolith => true,
        _ => false,
    };

    /// <summary>Returns <see langword="true"/> when <paramref name="kind"/> is an elemental resonance pylon that also discharges damage against nearby monsters (docs/adr/ADR-064).</summary>
    /// <param name="kind">Hazard or shrine kind.</param>
    /// <returns><see langword="true"/> for elemental resonance pylons and wayshrines.</returns>
    public static bool IsResonancePylon(DungeonHazardKind kind) => kind switch
    {
        DungeonHazardKind.ThunderPylon or DungeonHazardKind.SolarWayshrine or DungeonHazardKind.FrostMonolith => true,
        _ => false,
    };

    /// <summary>Returns the elemental discharge parameters against nearby monsters when a resonance pylon is triggered (docs/adr/ADR-064).</summary>
    /// <param name="kind">Pylon kind.</param>
    /// <returns>Tuple of <c>(MonsterDamage, RadiusTiles, SkillKind)</c>.</returns>
    public static (int MonsterDamage, int RadiusTiles, CombatSkillKind SkillKind) PylonDischarge(DungeonHazardKind kind) => kind switch
    {
        DungeonHazardKind.ThunderPylon => (18, 3, CombatSkillKind.Fireball),
        DungeonHazardKind.SolarWayshrine => (14, 3, CombatSkillKind.Whirlwind),
        DungeonHazardKind.FrostMonolith => (16, 3, CombatSkillKind.Fireball),
        _ => (0, 0, CombatSkillKind.None),
    };

    /// <summary>Returns the blessing rewards granted by a sanctuary shrine or resonance pylon (docs/adr/ADR-063, ADR-064).</summary>
    /// <param name="kind">Shrine kind.</param>
    /// <returns>Tuple of <c>(HealHp, RestoreMp, BonusXp, Buff)</c>.</returns>
    public static (int HealHp, int RestoreMp, int BonusXp, StatusEffectKind Buff) ShrineBlessing(DungeonHazardKind kind) => kind switch
    {
        DungeonHazardKind.VitalityShrine => (25, 0, 0, StatusEffectKind.Regeneration),
        DungeonHazardKind.ArcaneObelisk => (0, 20, 10, StatusEffectKind.None),
        DungeonHazardKind.EmpowermentAltar => (25, 20, 15, StatusEffectKind.Swiftness),
        DungeonHazardKind.ThunderPylon => (10, 20, 12, StatusEffectKind.Swiftness),
        DungeonHazardKind.SolarWayshrine => (30, 15, 12, StatusEffectKind.Regeneration),
        DungeonHazardKind.FrostMonolith => (20, 20, 12, StatusEffectKind.Regeneration),
        _ => (0, 0, 0, StatusEffectKind.None),
    };

    /// <summary>
    /// Deterministically plans beneficial Dungeon Blessing Shrines, Overworld Wayshrines, and Elemental Resonance Pylons on a combat map (docs/adr/ADR-063, ADR-064).
    /// </summary>
    /// <param name="mapId">Map id (e.g., <c>mine10</c>, <c>forest</c>, <c>mountain</c>).</param>
    /// <param name="seed">Deterministic instance seed.</param>
    /// <param name="grid">Map walkability grid.</param>
    /// <param name="spawnX">Player arrival tile X.</param>
    /// <param name="spawnY">Player arrival tile Y.</param>
    /// <param name="occupied">Optional set of already-occupied tiles to avoid.</param>
    /// <returns>Planned shrines and resonance pylons for the map.</returns>
    public static List<DungeonHazard> PlanShrines(
        string mapId,
        ulong seed,
        WalkGrid grid,
        int spawnX,
        int spawnY,
        IReadOnlySet<(int X, int Y)>? occupied = null)
    {
        ArgumentNullException.ThrowIfNull(mapId);
        ArgumentNullException.ThrowIfNull(grid);

        int floor = 0;
        bool isMine = mapId.StartsWith("mine", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(mapId.AsSpan(4), out floor)
            && floor >= 1
            && floor <= MineRules.MaxFloor;
        bool isWoods = string.Equals(mapId, GameMaps.Woods, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mapId, GameMaps.Forest, StringComparison.OrdinalIgnoreCase);
        bool isMountain = string.Equals(mapId, GameMaps.Mountain, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mapId, GameMaps.Backwoods, StringComparison.OrdinalIgnoreCase);

        if (!isMine && !isWoods && !isMountain)
        {
            return [];
        }

        int centerX = Math.Clamp(spawnX, 2, Math.Max(2, grid.Width - 3));
        int centerY = isMine
            ? Math.Clamp(Math.Min(spawnY, 6), 2, Math.Max(2, grid.Height - 3))
            : Math.Clamp(spawnY, 2, Math.Max(2, grid.Height - 3));

        List<(int X, int Y)> chamberCandidates = [];
        List<(int X, int Y)> fallbackCandidates = [];
        for (int y = 2; y < grid.Height - 2; y++)
        {
            for (int x = 2; x < grid.Width - 2; x++)
            {
                if (grid.IsBlocked(x, y) || (occupied is not null && occupied.Contains((x, y))))
                {
                    continue;
                }

                if (x == spawnX && y == spawnY)
                {
                    continue;
                }

                int chamberDist = Math.Max(Math.Abs(x - centerX), Math.Abs(y - centerY));
                int maxDist = isMine ? 3 : 6;
                if (chamberDist >= 1 && chamberDist <= maxDist)
                {
                    chamberCandidates.Add((x, y));
                }

                fallbackCandidates.Add((x, y));
            }
        }

        List<(int X, int Y)> candidates = chamberCandidates.Count >= 3 ? chamberCandidates : fallbackCandidates;
        if (candidates.Count == 0)
        {
            return [];
        }

        NodeRandom random = new(NodeRandom.Mix(seed, 0x5348524E, floor));
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        if (!isMine)
        {
            int wildCount = Math.Min(2, candidates.Count);
            List<DungeonHazard> wildShrines = new(wildCount);
            for (int i = 0; i < wildCount; i++)
            {
                (int x, int y) = candidates[i];
                DungeonHazardKind kind = i == 0
                    ? (isWoods ? DungeonHazardKind.SolarWayshrine : DungeonHazardKind.FrostMonolith)
                    : DungeonHazardKind.ThunderPylon;
                wildShrines.Add(new DungeonHazard((byte)(100 + i + 1), kind, (short)x, (short)y, 0));
            }

            return wildShrines;
        }

        bool isBossFloor = (floor % 10) == 0;
        int sanctuaryCount = isBossFloor ? 2 : 1;
        int totalCount = sanctuaryCount + 1;
        List<DungeonHazard> shrines = new(Math.Min(totalCount, candidates.Count));
        for (int i = 0; i < totalCount && i < candidates.Count; i++)
        {
            (int x, int y) = candidates[i];
            DungeonHazardKind kind;
            if (i < sanctuaryCount)
            {
                kind = isBossFloor
                    ? (i == 0 ? DungeonHazardKind.EmpowermentAltar : DungeonHazardKind.VitalityShrine)
                    : ((floor % 2 == 0) ? DungeonHazardKind.ArcaneObelisk : DungeonHazardKind.VitalityShrine);
            }
            else
            {
                kind = (floor % 3) switch
                {
                    1 => DungeonHazardKind.ThunderPylon,
                    2 => DungeonHazardKind.SolarWayshrine,
                    _ => DungeonHazardKind.FrostMonolith,
                };
            }

            shrines.Add(new DungeonHazard((byte)(100 + i + 1), kind, (short)x, (short)y, 0));
        }

        return shrines;
    }
}
