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
public readonly record struct MonsterSpawn(int MonsterId, MonsterKind Kind, int X, int Y, int Hp, int MaxHp);

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

    /// <summary>Selects a monster species for a given mine floor and spawn index.</summary>
    /// <param name="floor">Mine floor (1..40).</param>
    /// <param name="index">Spawn slot index.</param>
    /// <returns>The monster species.</returns>
    public static MonsterKind SpeciesForMineFloor(int floor, int index)
    {
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

        List<(int X, int Y)> candidates = [];
        for (int y = 2; y < grid.Height - 2; y++)
        {
            for (int x = 2; x < grid.Width - 2; x++)
            {
                if (!grid.IsBlocked(x, y)
                    && !occupied.Contains((x, y))
                    && Math.Max(Math.Abs(x - spawnX), Math.Abs(y - spawnY)) > 3)
                {
                    candidates.Add((x, y));
                }
            }
        }

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

            (int x, int y) = candidates[i];
            spawns.Add(new MonsterSpawn(i + 1, kind, x, y, def.MaxHp, def.MaxHp));
        }

        return spawns;
    }
}
