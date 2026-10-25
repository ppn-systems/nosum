// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Items;
using Nosum.Gameplay.Nodes;

namespace Nosum.Gameplay.Skills;

/// <summary>
/// Skill levels and the experience each action gives (docs/adr/ADR-023 D3). Levels are derived from cumulative experience and never stored.
/// The numbers are balancing data and may change without an ADR.
/// </summary>
public static class SkillRules
{
    /// <summary>The highest level.</summary>
    public const int MaxLevel = 10;

    /// <summary>Experience for tilling a tile.</summary>
    public const int TillXp = 3;

    /// <summary>Experience for watering a tile.</summary>
    public const int WaterXp = 2;

    /// <summary>Experience for harvesting a crop.</summary>
    public const int HarvestXp = 10;

    /// <summary>Experience for clearing empty tilled soil with the pickaxe.</summary>
    public const int ClearXp = 1;

    /// <summary>Experience for a swing that damages a node without breaking it.</summary>
    public const int HitXp = 1;

    /// <summary>The experience a skill stops counting at: the level-ten threshold.</summary>
    public const int XpCap = 15000;

    // Cumulative experience needed for level 1..10.
    private static readonly int[] s_thresholds = [100, 380, 770, 1300, 2150, 3300, 4800, 6900, 10000, 15000];

    /// <summary>Gets the cumulative experience needed for a level.</summary>
    /// <param name="level">A level from one to <see cref="MaxLevel"/>.</param>
    /// <returns>The experience; zero for level zero.</returns>
    public static int XpForLevel(int level) => level <= 0 ? 0 : s_thresholds[System.Math.Min(level, MaxLevel) - 1];

    /// <summary>Gets the level cumulative experience reaches.</summary>
    /// <param name="xp">Cumulative experience.</param>
    /// <returns>A level from zero to <see cref="MaxLevel"/>.</returns>
    public static int LevelOf(long xp)
    {
        int level = 0;
        while (level < MaxLevel && xp >= s_thresholds[level])
        {
            level++;
        }

        return level;
    }

    /// <summary>Gets the skill a tool trains and discounts energy for, for the tool's usual targets.</summary>
    /// <param name="tool">The tool.</param>
    /// <returns>The skill; <see cref="SkillKind.None"/> for a non-tool.</returns>
    public static SkillKind SkillOf(ToolKind tool) => tool switch
    {
        ToolKind.Hoe or ToolKind.WateringCan => SkillKind.Farming,
        ToolKind.Axe or ToolKind.Scythe => SkillKind.Foraging,
        ToolKind.Pickaxe => SkillKind.Mining,
        _ => SkillKind.None,
    };

    /// <summary>Gets the skill that working on a node trains.</summary>
    /// <param name="kind">The node kind.</param>
    /// <returns>Mining for stone and ore, foraging for everything else.</returns>
    public static SkillKind SkillOf(NodeKind kind) => kind is NodeKind.Stone or NodeKind.CopperOre ? SkillKind.Mining : SkillKind.Foraging;

    /// <summary>Gets the experience the hit that breaks a node gives.</summary>
    /// <param name="kind">The node kind.</param>
    /// <returns>The experience.</returns>
    public static int BreakXp(NodeKind kind) => kind switch
    {
        NodeKind.Tree => 12,
        NodeKind.Stump => 4,
        NodeKind.LargeStump => 8,
        NodeKind.Twig => 2,
        NodeKind.Weed => 1,
        NodeKind.Grass => 1,
        NodeKind.Stone => 4,
        NodeKind.CopperOre => 10,
        NodeKind.Forage => 7,
        _ => 0,
    };
}
