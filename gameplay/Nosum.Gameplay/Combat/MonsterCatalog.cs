// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Combat;

/// <summary>Catalogue of all monster species and their combat stats (docs/adr/ADR-045 D3).</summary>
public static class MonsterCatalog
{
    private static readonly MonsterDefinition[] s_all =
    [
        new(MonsterKind.GreenSlime, "green_slime", maxHp: 24, attackDamage: 5, xpReward: 6, ItemCatalog.SlimeGel, ItemCatalog.ParsnipSeeds),
        new(MonsterKind.FrostSlime, "frost_slime", maxHp: 45, attackDamage: 13, xpReward: 11, ItemCatalog.SlimeGel, ItemCatalog.ManaCrystal),
        new(MonsterKind.MagmaSlime, "magma_slime", maxHp: 75, attackDamage: 18, xpReward: 18, ItemCatalog.SlimeGel, ItemCatalog.Coal),
        new(MonsterKind.CaveBat, "cave_bat", maxHp: 30, attackDamage: 7, xpReward: 8, ItemCatalog.BatWing, ItemCatalog.CopperOre),
        new(MonsterKind.SporeShroom, "spore_shroom", maxHp: 40, attackDamage: 8, xpReward: 10, ItemCatalog.SporeCap, ItemCatalog.ManaCrystal),
        new(MonsterKind.GoblinScout, "goblin_scout", maxHp: 60, attackDamage: 16, xpReward: 15, ItemCatalog.GoblinEar, ItemCatalog.CopperBar),
        new(MonsterKind.SkeletonWarrior, "skeleton_warrior", maxHp: 90, attackDamage: 20, xpReward: 22, ItemCatalog.MonsterBone, ItemCatalog.NecromancerSkull),
        new(MonsterKind.StoneGolem, "stone_golem", maxHp: 140, attackDamage: 24, xpReward: 35, ItemCatalog.GolemCore, ItemCatalog.RunestoneTablet),
        new(MonsterKind.BossBroodmother, "boss_broodmother", maxHp: 180, attackDamage: 10, xpReward: 75, ItemCatalog.AncientScroll, ItemCatalog.NecromancerSkull, enrageThresholdPercent: 30),
        new(MonsterKind.BossGoblinChieftain, "boss_goblin_chieftain", maxHp: 280, attackDamage: 17, xpReward: 125, ItemCatalog.GoldenKey, ItemCatalog.GoblinEar, enrageThresholdPercent: 30),
        new(MonsterKind.BossMagmaColossus, "boss_magma_colossus", maxHp: 420, attackDamage: 19, xpReward: 200, ItemCatalog.ColossusCore, ItemCatalog.TreasureGem, enrageThresholdPercent: 30),
        new(MonsterKind.BossGolemKing, "boss_golem_king", maxHp: 600, attackDamage: 24, xpReward: 300, ItemCatalog.BossCrown, ItemCatalog.RunestoneTablet, enrageThresholdPercent: 30),
    ];

    private static readonly Dictionary<MonsterKind, MonsterDefinition> s_byKind = Index();

    /// <summary>Gets all monster definitions in kind order.</summary>
    public static IReadOnlyList<MonsterDefinition> All => s_all;

    /// <summary>Looks up a monster definition by <see cref="MonsterKind"/>.</summary>
    /// <param name="kind">Monster species.</param>
    /// <param name="definition">The definition when found.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public static bool TryGet(MonsterKind kind, [NotNullWhen(true)] out MonsterDefinition? definition)
        => s_byKind.TryGetValue(kind, out definition);

    private static Dictionary<MonsterKind, MonsterDefinition> Index()
    {
        Dictionary<MonsterKind, MonsterDefinition> map = new(s_all.Length);
        foreach (MonsterDefinition def in s_all)
        {
            map.Add(def.Kind, def);
        }

        return map;
    }
}
