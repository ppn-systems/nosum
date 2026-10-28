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
        MonsterCatalog.All.Should().HaveCount(12);
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

    [Fact]
    public void PlanMonsters_CanGenerateEliteModifiers_WithExpandedVitals()
    {
        WalkGrid grid = new(32, 32, 16, new bool[32 * 32]);
        HashSet<(int X, int Y)> occupied = [];

        bool foundElite = false;
        for (ulong seed = 100; seed < 120; seed++)
        {
            List<MonsterSpawn> spawns = CombatRules.PlanMonsters("mine5", seed, grid, occupied, 4, 4);
            foreach (MonsterSpawn m in spawns)
            {
                if (m.Modifier != MonsterModifier.None)
                {
                    foundElite = true;
                    if (m.Modifier == MonsterModifier.Shielded)
                    {
                        MonsterCatalog.TryGet(m.Kind, out MonsterDefinition? def);
                        m.MaxHp.Should().Be((int)(def!.MaxHp * 1.5));
                    }
                }
            }
        }

        foundElite.Should().BeTrue("mine floors should spawn elite monsters with affixes");
    }

    [Fact]
    public void MonsterDamage_AllSpeciesHaveNonZeroContactDamage()
    {
        CombatRules.AggroRadius.Should().Be(4);
        CombatRules.BossAggroRadius.Should().Be(6);
        foreach (MonsterKind kind in System.Enum.GetValues<MonsterKind>())
        {
            CombatRules.MonsterDamage(kind).Should().BeGreaterThan(0, $"{kind} deals contact damage");
        }
    }

    [Fact]
    public void BossSystem_IdentifiesBossesAndMilestoneSpawns()
    {
        CombatRules.IsBoss(MonsterKind.BossBroodmother).Should().BeTrue();
        CombatRules.IsBoss(MonsterKind.BossGoblinChieftain).Should().BeTrue();
        CombatRules.IsBoss(MonsterKind.BossMagmaColossus).Should().BeTrue();
        CombatRules.IsBoss(MonsterKind.BossGolemKing).Should().BeTrue();
        CombatRules.IsBoss(MonsterKind.GreenSlime).Should().BeFalse();

        CombatRules.IsBossFloor(10).Should().BeTrue();
        CombatRules.IsBossFloor(20).Should().BeTrue();
        CombatRules.IsBossFloor(30).Should().BeTrue();
        CombatRules.IsBossFloor(40).Should().BeTrue();
        CombatRules.IsBossFloor(5).Should().BeFalse();
        CombatRules.IsBossFloor(15).Should().BeFalse();

        CombatRules.SpeciesForMineFloor(10, 0).Should().Be(MonsterKind.BossBroodmother);
        CombatRules.SpeciesForMineFloor(20, 0).Should().Be(MonsterKind.BossGoblinChieftain);
        CombatRules.SpeciesForMineFloor(30, 0).Should().Be(MonsterKind.BossMagmaColossus);
        CombatRules.SpeciesForMineFloor(40, 0).Should().Be(MonsterKind.BossGolemKing);

        MonsterCatalog.TryGet(MonsterKind.BossBroodmother, out MonsterDefinition? broodmother).Should().BeTrue();
        broodmother!.PrimaryDropItemId.Should().Be(ItemCatalog.AncientScroll);

        MonsterCatalog.TryGet(MonsterKind.BossGoblinChieftain, out MonsterDefinition? chieftain).Should().BeTrue();
        chieftain!.PrimaryDropItemId.Should().Be(ItemCatalog.GoldenKey);

        MonsterCatalog.TryGet(MonsterKind.BossMagmaColossus, out MonsterDefinition? colossus).Should().BeTrue();
        colossus!.PrimaryDropItemId.Should().Be(ItemCatalog.ColossusCore);

        MonsterCatalog.TryGet(MonsterKind.BossGolemKing, out MonsterDefinition? golemKing).Should().BeTrue();
        golemKing!.PrimaryDropItemId.Should().Be(ItemCatalog.BossCrown);
    }

    [Fact]
    public void DefenseAndArmorSystem_CalculatesDefenseAndMitigatesDamage()
    {
        CombatRules.ArmorDefense(ItemCatalog.BossCrown).Should().Be(8);
        CombatRules.ArmorDefense(ItemCatalog.IronChestplate).Should().Be(7);
        CombatRules.ArmorDefense(ItemCatalog.IronGreaves).Should().Be(5);
        CombatRules.ArmorDefense(ItemCatalog.IronHelmet).Should().Be(4);
        CombatRules.ArmorDefense(ItemCatalog.LeatherArmor).Should().Be(3);
        CombatRules.ArmorDefense(ItemCatalog.Wood).Should().Be(0);

        int totalDefense = CombatRules.CalculateDefense(ItemCatalog.IronHelmet, ItemCatalog.IronChestplate, ItemCatalog.IronGreaves);
        totalDefense.Should().Be(4 + 7 + 5);

        // Raw damage 20 mitigated by 16 defense -> 4 damage
        CombatRules.MitigateDamage(20, totalDefense).Should().Be(4);

        // High defense still allows minimum 1 damage
        CombatRules.MitigateDamage(10, 50).Should().Be(1);
    }

    [Fact]
    public void StatusEffectSystem_InflictsPoisonAndPotionRegeneration()
    {
        (StatusEffectKind elixirEffect, int elixirDur, int elixirMag) = CombatRules.PotionEffect(ItemCatalog.Elixir);
        elixirEffect.Should().Be(StatusEffectKind.Regeneration);
        elixirDur.Should().Be(30);
        elixirMag.Should().Be(5);

        (StatusEffectKind soupEffect, int soupDur, int soupMag) = CombatRules.PotionEffect(ItemCatalog.PumpkinSoup);
        soupEffect.Should().Be(StatusEffectKind.Swiftness);
        soupDur.Should().Be(20);
        soupMag.Should().Be(1);

        (StatusEffectKind shroomDebuff, int shroomDur, int shroomMag) = CombatRules.MonsterStatusOnHit(MonsterKind.SporeShroom);
        shroomDebuff.Should().Be(StatusEffectKind.Poison);
        shroomDur.Should().Be(10);
        shroomMag.Should().Be(3);

        (StatusEffectKind slimeDebuff, _, _) = CombatRules.MonsterStatusOnHit(MonsterKind.GreenSlime);
        slimeDebuff.Should().Be(StatusEffectKind.None);
    }

    [Fact]
    public void CoopCombatRules_ProximityAndPartyParameters_AreConfigured()
    {
        CombatRules.CoopShareRadius.Should().Be(12);
        CombatRules.AggroRadius.Should().Be(4);
        CombatRules.BossAggroRadius.Should().Be(6);
    }
}
