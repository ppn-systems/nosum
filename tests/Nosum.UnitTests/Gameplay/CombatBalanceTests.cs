// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Skills;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>
/// Locks the combat balance targets of <c>docs/design/combat-balance.md</c> (the ADR-045 amendment). Every mine band has a reference player: the
/// weapon, armor and Combat level it is expected to carry. The fights are modelled with the same numbers the server uses: a hit lands every
/// sword swing, a monster strikes back on each non-lethal hit within reach, and it strikes once per AI tick while the fight lasts.
/// </summary>
public sealed class CombatBalanceTests
{
    // The sword swing as the client paces it (ToolPacing.SwingMs for a sword plus RecoveryMs) and the monster AI tick (CombatService.DefaultAiInterval).
    private const int SwingMs = 360 + 80;
    private const int AiTickMs = 1500;

    // Mean XP of a base reward, with 35% of mob kills elite at 2.5 times the XP (CombatRules.EliteXpMultiplier, MonsterSpawnPlanner.EliteChancePercent).
    private const double ExpectedEliteXpFactor = 0.65 + (0.35 * CombatRules.EliteXpMultiplier);

    private sealed record Band(int Number, int FirstFloor, int LastFloor, int Level, int WeaponItemId, int HatItemId, int ShirtItemId, int PantsItemId)
    {
        public int Defense => CombatRules.CalculateDefense(HatItemId, ShirtItemId, PantsItemId);

        public int Damage => CombatRules.CalculateDamage(WeaponItemId, Level);

        public int MaxHp => CombatRules.MaxHpForLevel(Level);

        public int BossFloor => LastFloor;
    }

    // The reference player of each band: bronze in band 1, iron in band 2 (iron ore comes from floor 20), steel in band 3 and gold in band 4.
    private static readonly Band[] s_bands =
    [
        new(1, 1, 10, 2, ItemCatalog.BronzeSword, 0, ItemCatalog.LeatherArmor, 0),
        new(2, 11, 20, 4, ItemCatalog.IronSword, ItemCatalog.IronHelmet, ItemCatalog.IronChestplate, ItemCatalog.IronGreaves),
        new(3, 21, 30, 5, ItemCatalog.SteelSword, ItemCatalog.IronHelmet, ItemCatalog.IronChestplate, ItemCatalog.IronGreaves),
        new(4, 31, 40, 7, ItemCatalog.GoldSword, ItemCatalog.BossCrown, ItemCatalog.IronChestplate, ItemCatalog.IronGreaves),
    ];

    /// <summary>The normal (non-boss) species a band's floors draw from, by spawn slot.</summary>
    private static IEnumerable<MonsterDefinition> NormalMonstersOf(Band band)
    {
        for (int slot = 0; slot < 3; slot++)
        {
            MonsterKind kind = MonsterSpawnPlanner.SpeciesForMineFloor(band.FirstFloor, slot);
            MonsterCatalog.TryGet(kind, out MonsterDefinition? def).Should().BeTrue();
            yield return def!;
        }
    }

    private static MonsterDefinition BossOf(Band band)
    {
        MonsterKind kind = MonsterSpawnPlanner.BossForFloor(band.BossFloor);
        MonsterCatalog.TryGet(kind, out MonsterDefinition? def).Should().BeTrue();
        return def!;
    }

    private static int HitsToKill(MonsterDefinition def, int damage) => (def.MaxHp + damage - 1) / damage;

    /// <summary>Damage the player takes killing the monster: one counter per non-lethal hit plus one contact per AI tick of the fight.</summary>
    private static int DamageTakenToKill(MonsterDefinition def, Band band)
    {
        int hits = HitsToKill(def, band.Damage);
        int contacts = hits * SwingMs / AiTickMs;
        return ((hits - 1) + contacts) * CombatRules.MitigateDamage(def.AttackDamage, band.Defense);
    }

    /// <summary>Monster hits that the player, wearing the band's armor, survives.</summary>
    private static int HitsToDie(MonsterDefinition def, int defense, int maxHp) => (maxHp + CombatRules.MitigateDamage(def.AttackDamage, defense) - 1) / CombatRules.MitigateDamage(def.AttackDamage, defense);

    [Fact]
    public void NormalMonsters_OfEveryBand_DieInOneOrTwoHitsOfTheReferenceWeapon()
    {
        foreach (Band band in s_bands)
        {
            foreach (MonsterDefinition def in NormalMonstersOf(band))
            {
                HitsToKill(def, band.Damage).Should().BeInRange(1, 2, $"band {band.Number} {def.Key} with the reference weapon");
            }
        }
    }

    [Fact]
    public void BossOfEveryBand_DiesInSixToNineHitsOfTheReferenceWeapon()
    {
        foreach (Band band in s_bands)
        {
            HitsToKill(BossOf(band), band.Damage).Should().BeInRange(6, 9, $"boss of band {band.Number}");
        }
    }

    [Fact]
    public void BossOfEveryBand_CostsTheReferencePlayerBetween35And65PercentOfHp()
    {
        foreach (Band band in s_bands)
        {
            int taken = DamageTakenToKill(BossOf(band), band);
            int percent = taken * 100 / band.MaxHp;
            percent.Should().BeInRange(35, 65, $"boss of band {band.Number} takes {taken} of {band.MaxHp} HP");
        }
    }

    [Fact]
    public void NormalMonsters_CostTheReferencePlayerAtMostEightPercentOfHpPerKill()
    {
        foreach (Band band in s_bands)
        {
            foreach (MonsterDefinition def in NormalMonstersOf(band))
            {
                int percent = DamageTakenToKill(def, band) * 100 / band.MaxHp;
                percent.Should().BeLessThanOrEqualTo(8, $"band {band.Number} {def.Key} with the reference armor");
            }
        }
    }

    [Fact]
    public void ReferenceArmor_LetsAPlayerSurviveFifteenHitsOfTheStrongestNormalMonster()
    {
        foreach (Band band in s_bands)
        {
            MonsterDefinition strongest = NormalMonstersOf(band).OrderByDescending(m => m.AttackDamage).First();
            HitsToDie(strongest, band.Defense, band.MaxHp).Should().BeGreaterThanOrEqualTo(15, $"band {band.Number} {strongest.Key} against the reference armor");
        }
    }

    [Fact]
    public void Armor_AddsAtLeastFiveHitsOfSurvivalAgainstTheStrongestNormalMonster()
    {
        foreach (Band band in s_bands)
        {
            MonsterDefinition strongest = NormalMonstersOf(band).OrderByDescending(m => m.AttackDamage).First();
            int withArmor = HitsToDie(strongest, band.Defense, band.MaxHp);
            int withoutArmor = HitsToDie(strongest, 0, band.MaxHp);
            (withArmor - withoutArmor).Should().BeGreaterThanOrEqualTo(5, $"band {band.Number}: the armor is worth crafting");
        }
    }

    [Fact]
    public void WeaponLadder_EachTierIsAtLeastFortyPercentStrongerThanTheLast()
    {
        int[] ladder = [ItemCatalog.WoodenSword, ItemCatalog.BronzeSword, ItemCatalog.IronSword, ItemCatalog.SteelSword, ItemCatalog.GoldSword];
        for (int i = 1; i < ladder.Length; i++)
        {
            int previous = CombatRules.WeaponDamage(ladder[i - 1]);
            int current = CombatRules.WeaponDamage(ladder[i]);
            current.Should().BeGreaterThanOrEqualTo((previous * 14) / 10, $"tier {i} against tier {i - 1}");
        }
    }

    [Fact]
    public void ArmorLadder_EachPieceBeatsTheCheaperOneBeforeIt()
    {
        CombatRules.ArmorDefense(ItemCatalog.LeatherArmor).Should().BeLessThan(CombatRules.ArmorDefense(ItemCatalog.RangerTunic));
        CombatRules.ArmorDefense(ItemCatalog.RangerTunic).Should().BeLessThan(CombatRules.ArmorDefense(ItemCatalog.IronChestplate));
        CombatRules.ArmorDefense(ItemCatalog.IronHelmet).Should().BeLessThan(CombatRules.ArmorDefense(ItemCatalog.BossCrown));
    }

    [Fact]
    public void HealthPotion_RestoresBetween25And50PercentOfEveryBandsMaxHp()
    {
        int heal = CombatRules.PotionRestore(ItemCatalog.HealthPotion).Hp;
        foreach (Band band in s_bands)
        {
            int percent = heal * 100 / band.MaxHp;
            percent.Should().BeInRange(25, 50, $"health potion against the max HP of band {band.Number}");
        }
    }

    [Fact]
    public void MonsterPrimaryDrops_AllHaveASellPrice_SoNoDropIsWorthless()
    {
        foreach (MonsterDefinition def in MonsterCatalog.All)
        {
            ItemCatalog.TryGet(def.PrimaryDropItemId, out ItemDefinition? item).Should().BeTrue();
            item!.SellPrice.Should().BeGreaterThan(0, $"the primary drop of {def.Key} is {item.Key}");
        }
    }

    [Theory]
    [InlineData(1, 4, 10)]
    [InlineData(2, 4, 12)]
    [InlineData(3, 6, 12)]
    [InlineData(4, 10, 20)]
    public void NormalMonsterDrops_MeanSellValueStaysInItsBandsRange(int band, int minCoins, int maxCoins)
    {
        Band reference = s_bands[band - 1];
        List<int> values = NormalMonstersOf(reference)
            .Select(def => ItemCatalog.TryGet(def.PrimaryDropItemId, out ItemDefinition? item) ? item.SellPrice : 0)
            .ToList();
        double mean = values.Average();
        mean.Should().BeInRange(minCoins, maxCoins, $"band {band} primary drops sell for {string.Join(", ", values)}");
    }

    [Fact]
    public void TreasureGem_FromAnEliteKill_SellsForNoMoreThanFortyCoins()
    {
        ItemCatalog.TryGet(ItemCatalog.TreasureGem, out ItemDefinition? gem).Should().BeTrue();
        gem!.SellPrice.Should().BeInRange(1, 40);
    }

    [Fact]
    public void FullClear_OfEachBand_LeavesThePlayerAtTheBandsReferenceLevel()
    {
        long cumulative = 0;
        foreach (Band band in s_bands)
        {
            cumulative += BandExperience(band);
            SkillRules.LevelOf(cumulative).Should().Be(band.Level, $"after clearing band {band.Number} ({cumulative} XP)");
        }
    }

    [Fact]
    public void FullClear_OfTheWholeMine_TakesAboutThreeDaysToReachCombatLevelTen()
    {
        long total = s_bands.Sum(BandExperience);
        total.Should().BeInRange(5000, 6500, "the XP of one full clear of the mine");
        (SkillRules.XpCap / (double)total).Should().BeInRange(2.0, 3.5, "full clears to reach level ten");
    }

    /// <summary>Expected combat XP of one clear of a band: its mob kills (elites included on average) and its boss kill.</summary>
    private static long BandExperience(Band band)
    {
        double xp = 0;
        for (int floor = band.FirstFloor; floor <= band.LastFloor; floor++)
        {
            int count = Math.Clamp(3 + (floor / 8), 3, 8);
            for (int slot = 0; slot < count; slot++)
            {
                MonsterKind kind = MonsterSpawnPlanner.SpeciesForMineFloor(floor, slot);
                if (MonsterSpawnPlanner.IsBoss(kind))
                {
                    continue;
                }

                MonsterCatalog.TryGet(kind, out MonsterDefinition? def).Should().BeTrue();
                xp += def!.XpReward * ExpectedEliteXpFactor;
            }

            if (MonsterSpawnPlanner.IsBossFloor(floor))
            {
                MonsterCatalog.TryGet(MonsterSpawnPlanner.BossForFloor(floor), out MonsterDefinition? boss).Should().BeTrue();
                xp += boss!.XpReward;
            }
        }

        return (long)Math.Round(xp);
    }

    [Fact]
    public void SpeciesOfEachBand_AreTheBiomeTierSpecies()
    {
        // The species breaks follow the biome tiers (ADR-070): a band never mixes species from its neighbours.
        Dictionary<int, MonsterKind[]> expected = new()
        {
            [1] = [MonsterKind.GreenSlime, MonsterKind.CaveBat, MonsterKind.SporeShroom],
            [2] = [MonsterKind.FrostSlime, MonsterKind.GoblinScout, MonsterKind.SporeShroom],
            [3] = [MonsterKind.MagmaSlime, MonsterKind.SkeletonWarrior, MonsterKind.GoblinScout],
            [4] = [MonsterKind.StoneGolem, MonsterKind.SkeletonWarrior, MonsterKind.MagmaSlime],
        };

        foreach (Band band in s_bands)
        {
            for (int floor = band.FirstFloor; floor <= band.LastFloor; floor++)
            {
                for (int slot = 0; slot < 8; slot++)
                {
                    MonsterKind kind = MonsterSpawnPlanner.SpeciesForMineFloor(floor, slot);
                    if (MonsterSpawnPlanner.IsBoss(kind))
                    {
                        continue;
                    }

                    expected[band.Number].Should().Contain(kind, $"floor {floor} slot {slot}");
                }
            }
        }
    }
}
