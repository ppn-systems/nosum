// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Unit tests for MMORPG vitals (HP/MP), weapons, potions, monster catalogue, and floor planning (docs/adr/ADR-045).</summary>
public sealed class CombatRulesTests
{
    [Theory]
    [InlineData(0, 100, 50)]
    [InlineData(1, 110, 55)]
    [InlineData(5, 150, 75)]
    [InlineData(10, 200, 100)]
    public void MaxHpAndMaxMp_ScaleWithCombatLevel(int level, int expectedHp, int expectedMp)
    {
        CombatRules.MaxHpForLevel(level).Should().Be(expectedHp);
        CombatRules.MaxMpForLevel(level).Should().Be(expectedMp);
    }

    [Fact]
    public void MonsterCatalog_EveryMonsterHasValidStatsAndExistingDrops()
    {
        MonsterCatalog.All.Should().HaveCount(8);
        MonsterCatalog.All.Select(m => m.Kind).Should().OnlyHaveUniqueItems();
        MonsterCatalog.All.Select(m => m.Key).Should().OnlyHaveUniqueItems();

        foreach (MonsterDefinition def in MonsterCatalog.All)
        {
            MonsterCatalog.TryGet(def.Kind, out MonsterDefinition? found).Should().BeTrue();
            found.Should().BeSameAs(def);
            def.MaxHp.Should().BeGreaterThan(0);
            def.AttackDamage.Should().BeGreaterThan(0);
            def.XpReward.Should().BeGreaterThan(0);
            ItemCatalog.TryGet(def.PrimaryDropItemId, out _).Should().BeTrue($"{def.Key} primary drop exists");
            ItemCatalog.TryGet(def.RareDropItemId, out _).Should().BeTrue($"{def.Key} rare drop exists");
        }
    }

    [Theory]
    [InlineData(ItemCatalog.WoodenSword, 0, 12, 0, 2)]
    [InlineData(ItemCatalog.BronzeSword, 2, 24, 0, 2)]
    [InlineData(ItemCatalog.IronSword, 5, 42, 0, 2)]
    [InlineData(ItemCatalog.SteelSword, 5, 58, 0, 2)]
    [InlineData(ItemCatalog.GoldSword, 10, 88, 0, 2)]
    [InlineData(ItemCatalog.ArcaneStaff, 3, 51, 10, 4)]
    public void Weapons_DealExpectedDamageMpCostAndRange(int itemId, int level, int expectedDamage, int expectedMp, int expectedRange)
    {
        CombatRules.CalculateDamage(itemId, level).Should().Be(expectedDamage);
        CombatRules.AttackMpCost(itemId).Should().Be(expectedMp);
        CombatRules.AttackRange(itemId).Should().Be(expectedRange);
    }

    [Fact]
    public void Potions_RestoreExpectedHpAndMp()
    {
        CombatRules.IsPotion(ItemCatalog.HealthPotion).Should().BeTrue();
        CombatRules.PotionRestore(ItemCatalog.HealthPotion).Should().Be((50, 0));

        CombatRules.IsPotion(ItemCatalog.ManaPotion).Should().BeTrue();
        CombatRules.PotionRestore(ItemCatalog.ManaPotion).Should().Be((0, 35));

        CombatRules.IsPotion(ItemCatalog.Elixir).Should().BeTrue();
        CombatRules.PotionRestore(ItemCatalog.Elixir).Should().Be((100, 50));

        CombatRules.IsPotion(ItemCatalog.Wood).Should().BeFalse();
    }

    [Fact]
    public void PlanMonsters_SpawnsDeterministicMonstersOnMineAndWildernessMaps()
    {
        WalkGrid grid = new(16, 16, 16, new bool[16 * 16]);
        HashSet<(int X, int Y)> occupied = [];

        List<MonsterSpawn> mine1A = CombatRules.PlanMonsters("mine1", 12345UL, grid, occupied, 4, 4);
        List<MonsterSpawn> mine1B = CombatRules.PlanMonsters("mine1", 12345UL, grid, occupied, 4, 4);
        mine1A.Should().NotBeEmpty();
        mine1A.Should().Equal(mine1B);

        List<MonsterSpawn> woods = CombatRules.PlanMonsters("Woods", 67890UL, grid, occupied, 4, 4);
        woods.Should().NotBeEmpty();

        List<MonsterSpawn> farm = CombatRules.PlanMonsters("Farm", 12345UL, grid, occupied, 4, 4);
        farm.Should().BeEmpty("the player's farm does not spawn hostile monsters");
    }
}
