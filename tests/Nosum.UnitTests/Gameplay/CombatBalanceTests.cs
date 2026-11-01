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

    /// <summary>
    /// Damage the player takes killing a boss: the same counters and contacts as a normal fight, plus one telegraphed ground slam per
    /// slam period for the whole fight, none dodged (<see cref="CombatRules.GroundSlamDamage"/>).
    /// </summary>
    private static int BossDamageTaken(MonsterDefinition def, Band band)
    {
        int hits = HitsToKill(def, band.Damage);
        int timeToKillMs = hits * SwingMs;
        int contacts = timeToKillMs / AiTickMs;
        int slams = (timeToKillMs / (CombatRules.GroundSlamTelegraphMs + CombatRules.GroundSlamCooldownMs)) + 1;
        int counter = CombatRules.MitigateDamage(def.AttackDamage, band.Defense);
        int slam = CombatRules.MitigateDamage(CombatRules.GroundSlamDamage(def.Kind), band.Defense);
        return ((hits - 1 + contacts) * counter) + (slams * slam);
    }

    /// <summary>Monster hits that the player, wearing the band's armor, survives.</summary>
    private static int HitsToDie(MonsterDefinition def, int defense, int maxHp) => (maxHp + CombatRules.MitigateDamage(def.AttackDamage, defense) - 1) / CombatRules.MitigateDamage(def.AttackDamage, defense);

    [Fact]
    public void NormalMonsters_OfEveryBand_TakeThreeToFiveHitsOfTheReferenceWeapon()
    {
        foreach (Band band in s_bands)
        {
            foreach (MonsterDefinition def in NormalMonstersOf(band))
            {
                HitsToKill(def, band.Damage).Should().BeInRange(3, 5, $"band {band.Number} {def.Key} with the reference weapon");
            }
        }
    }

    [Fact]
    public void BossOfEveryBand_TakesBetweenTwentyAndFortySecondsToKill()
    {
        foreach (Band band in s_bands)
        {
            int timeToKillMs = HitsToKill(BossOf(band), band.Damage) * SwingMs;
            timeToKillMs.Should().BeInRange(20_000, 40_000, $"boss of band {band.Number} with the reference weapon");
        }
    }

    [Fact]
    public void BossOfEveryBand_CostsTheReferencePlayerBetween40And70PercentOfHp_WhenTheSlamsAreNotDodged()
    {
        foreach (Band band in s_bands)
        {
            int taken = BossDamageTaken(BossOf(band), band);
            int percent = taken * 100 / band.MaxHp;
            percent.Should().BeInRange(40, 70, $"boss of band {band.Number} takes {taken} of {band.MaxHp} HP");
        }
    }

    [Fact]
    public void NormalMonsters_CostTheReferencePlayerAtMostTwentyPercentOfHpPerKill()
    {
        foreach (Band band in s_bands)
        {
            foreach (MonsterDefinition def in NormalMonstersOf(band))
            {
                int percent = DamageTakenToKill(def, band) * 100 / band.MaxHp;
                percent.Should().BeLessThanOrEqualTo(20, $"band {band.Number} {def.Key} with the reference armor");
            }
        }
    }

    [Fact]
    public void NormalMonsters_CostTheReferencePlayerAtLeastTenPercentOfHpPerKill_ExceptTheSharedSporeShroomOfBandTwo()
    {
        // The spore shroom is one species in bands 1 and 2, and no single HP and attack meets both bands' 10% floor (see docs/design/combat-balance.md, known gaps).
        foreach (Band band in s_bands)
        {
            foreach (MonsterDefinition def in NormalMonstersOf(band))
            {
                if (band.Number == 2 && def.Kind == MonsterKind.SporeShroom)
                {
                    continue;
                }

                int percent = DamageTakenToKill(def, band) * 100 / band.MaxHp;
                percent.Should().BeGreaterThanOrEqualTo(10, $"band {band.Number} {def.Key} with the reference armor");
            }
        }
    }

    [Fact]
    public void ReferenceArmor_LetsAPlayerSurviveTenHitsOfTheStrongestNormalMonster()
    {
        // The 6-to-10 hits-to-die target conflicts with the 10-to-20% per-kill target (see docs/design/combat-balance.md, known gaps); this locks the achieved floor.
        foreach (Band band in s_bands)
        {
            MonsterDefinition strongest = NormalMonstersOf(band).OrderByDescending(m => m.AttackDamage).First();
            HitsToDie(strongest, band.Defense, band.MaxHp).Should().BeGreaterThanOrEqualTo(10, $"band {band.Number} {strongest.Key} against the reference armor");
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

    /// <summary>Normal-fight damage the reference player takes on a full clear of a band (bosses excluded, as in the potion table).</summary>
    private static int BandNormalDamage(Band band)
    {
        int total = 0;
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
                total += DamageTakenToKill(def!, band);
            }
        }

        return total;
    }

    [Fact]
    public void BandClear_NeedsBetweenTwoAndSixHealthPotions()
    {
        // A potion restores 215 HP, enough for band 3 and 4 in about six potions (see docs/design/combat-balance.md, Potions).
        int heal = CombatRules.PotionRestore(ItemCatalog.HealthPotion).Hp;
        foreach (Band band in s_bands)
        {
            int potions = (BandNormalDamage(band) + heal - 1) / heal;
            potions.Should().BeInRange(2, 6, $"a full clear of band {band.Number} takes {BandNormalDamage(band)} damage");
        }
    }

    [Fact]
    public void HealthPotion_CostsSlimeGelThatTheBandsSlimesSupply()
    {
        // Two slime gel and a fiber make one potion. The band's slimes must supply the gel for the potions its clear needs.
        int heal = CombatRules.PotionRestore(ItemCatalog.HealthPotion).Hp;
        foreach (Band band in s_bands)
        {
            int potions = (BandNormalDamage(band) + heal - 1) / heal;
            int gelNeeded = potions * 2;
            int slimeKills = NormalKillsOfSpecies(band, MonsterKind.GreenSlime, MonsterKind.FrostSlime, MonsterKind.MagmaSlime);
            slimeKills.Should().BeGreaterThanOrEqualTo(gelNeeded, $"band {band.Number} slimes must supply the gel for {potions} potions");
        }
    }

    private static int NormalKillsOfSpecies(Band band, params MonsterKind[] kinds)
    {
        int count = 0;
        for (int floor = band.FirstFloor; floor <= band.LastFloor; floor++)
        {
            int slots = Math.Clamp(3 + (floor / 8), 3, 8);
            for (int slot = 0; slot < slots; slot++)
            {
                MonsterKind kind = MonsterSpawnPlanner.SpeciesForMineFloor(floor, slot);
                if (!MonsterSpawnPlanner.IsBoss(kind) && kinds.Contains(kind))
                {
                    count++;
                }
            }
        }

        return count;
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

    /// <summary>
    /// Expected sell value of one full clear of a band: each normal kill's primary drop, its rare drop (half the time, or always from an elite)
    /// and an elite's treasure gem (35% of kills), plus the boss's primary drop and its rare drop half the time.
    /// </summary>
    private static double BandDropValue(Band band)
    {
        const double rareChanceNormal = 0.5;
        const double elitePercent = MonsterSpawnPlanner.EliteChancePercent / 100.0;
        double rareExpectedPerNormalKill = ((1 - elitePercent) * rareChanceNormal) + elitePercent;
        int gemValue = SellPriceOf(ItemCatalog.TreasureGem);

        double total = 0;
        for (int floor = band.FirstFloor; floor <= band.LastFloor; floor++)
        {
            int count = Math.Clamp(3 + (floor / 8), 3, 8);
            for (int slot = 0; slot < count; slot++)
            {
                MonsterKind kind = MonsterSpawnPlanner.SpeciesForMineFloor(floor, slot);
                MonsterCatalog.TryGet(kind, out MonsterDefinition? def).Should().BeTrue();
                if (MonsterSpawnPlanner.IsBoss(kind))
                {
                    total += SellPriceOf(def!.PrimaryDropItemId) + (0.5 * SellPriceOf(def.RareDropItemId));
                    continue;
                }

                total += SellPriceOf(def!.PrimaryDropItemId) + (rareExpectedPerNormalKill * SellPriceOf(def.RareDropItemId)) + (elitePercent * gemValue);
            }
        }

        return total;
    }

    private static int SellPriceOf(int itemId)
    {
        ItemCatalog.TryGet(itemId, out ItemDefinition? item).Should().BeTrue();
        return item!.SellPrice;
    }

    [Theory]
    [InlineData(1, 100, 135)]
    [InlineData(2, 150, 200)]
    [InlineData(3, 600, 800)]
    public void BandClear_PaysAboutTheBandsDropValueTarget(int band, int minCoins, int maxCoins)
    {
        // Bands 1 and 2 pay a quarter to a third of the next band's gear: iron greaves (400) and iron chestplate (600).
        // Band 3's value is locked on its own, since the gear it leads to is the boss crown and that is a drop too.
        double value = BandDropValue(s_bands[band - 1]);
        value.Should().BeInRange(minCoins, maxCoins, $"a full clear of band {band} pays {value:F0} coins");
    }

    [Fact]
    public void BandClear_OfBandOne_PaysAQuarterToAThirdOfBandTwoGear()
    {
        int greaves = SellPriceOf(ItemCatalog.IronGreaves);
        double value = BandDropValue(s_bands[0]);
        (value * 4).Should().BeGreaterThanOrEqualTo(greaves, "a band one clear pays at most a third of band two gear");
        (value * 3).Should().BeLessThanOrEqualTo(greaves, "a band one clear pays at least a quarter of band two gear");
    }

    [Fact]
    public void BandClear_OfBandTwo_PaysAQuarterToAThirdOfBandThreeGear()
    {
        int chestplate = SellPriceOf(ItemCatalog.IronChestplate);
        double value = BandDropValue(s_bands[1]);
        (value * 4).Should().BeGreaterThanOrEqualTo(chestplate, "a band two clear pays at most a third of band three gear");
        (value * 3).Should().BeLessThanOrEqualTo(chestplate, "a band two clear pays at least a quarter of band three gear");
    }

    [Fact]
    public void BandClear_RisesWithEveryBand_AndTheLastBandStaysWithinTwiceTheOneBefore()
    {
        double[] values = s_bands.Select(BandDropValue).ToArray();
        for (int i = 1; i < values.Length; i++)
        {
            values[i].Should().BeGreaterThan(values[i - 1], $"band {i + 1} pays more than band {i}");
        }

        values[3].Should().BeLessThanOrEqualTo(2 * values[2], "band 4 pays at most twice band 3");
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
