// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Pets;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class PetRulesTests
{
    [Theory]
    [InlineData((byte)PetKind.Cat, 600)]
    [InlineData((byte)PetKind.Dog, 900)]
    [InlineData((byte)PetKind.None, 0)]
    [InlineData((byte)3, 0)]
    public void PriceOf_IsTheCarpentersPriceForTheKind_AndZeroForNothingElse(byte kind, long price)
    {
        PetRules.PriceOf(kind).Should().Be(price);
    }

    [Theory]
    [InlineData((byte)PetKind.Cat, 300)]
    [InlineData((byte)PetKind.Dog, 450)]
    [InlineData((byte)PetKind.None, 0)]
    public void RehomeRefundOf_IsHalfThePriceRoundedDown(byte kind, long refund)
    {
        PetRules.RehomeRefundOf(kind).Should().Be(refund);
        PetRules.RehomeRefundOf(kind).Should().BeLessThanOrEqualTo(PetRules.PriceOf(kind) / 2);
    }

    [Fact]
    public void CleanName_MasksProfanityAndCollapsesSpaces_AsChatDoes()
    {
        PetRules.CleanName("  Fuck   Mochi  ").Should().Be("**** Mochi");
    }

    [Fact]
    public void CleanName_CutsToTheMaximumLength()
    {
        string name = PetRules.CleanName(new string('a', 40));

        name.Should().HaveLength(PetRules.MaxNameLength);
    }

    [Fact]
    public void CleanName_NothingPrintable_IsEmpty()
    {
        PetRules.CleanName("   \t ").Should().BeEmpty();
        PetRules.CleanName(null).Should().BeEmpty();
    }

    [Fact]
    public void CanPet_OncePerGameDay()
    {
        PetRules.CanPet(lastPettedDay: 0, today: 1).Should().BeTrue("a new pet has never been petted");
        PetRules.CanPet(lastPettedDay: 4, today: 4).Should().BeFalse("petted already today");
        PetRules.CanPet(lastPettedDay: 4, today: 5).Should().BeTrue("a new game day");
    }

    [Fact]
    public void Petted_RaisesTheMood_AndNeverPassesTheMaximum()
    {
        PetRules.Petted(50).Should().Be(50 + PetRules.PettingMood);
        PetRules.Petted(PetRules.MaxMood).Should().Be(PetRules.MaxMood);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void IsAdoptable_OnlyACatOrADog(byte kind, bool expected)
    {
        PetRules.IsAdoptable(kind).Should().Be(expected);
    }

    [Fact]
    public void ForageOf_IsTheSameAnswerForTheSamePlayerAndDay()
    {
        Guid player = Guid.Parse("6f1d2c3a-4b5e-4f70-8a9b-0c1d2e3f4a5b");

        for (int day = 1; day < 50; day++)
        {
            PetRules.ForageOf(player, day).Should().Be(PetRules.ForageOf(player, day));
        }
    }

    [Fact]
    public void ForageOf_OnlyEverGivesAForageItemOrNothing()
    {
        for (int i = 0; i < 2000; i++)
        {
            int item = PetRules.ForageOf(Guid.NewGuid(), 1 + (i % 200));
            (item == 0 || PetRules.ForageItems.Contains(item)).Should().BeTrue();
        }
    }

    [Fact]
    public void ForageOf_GivesAboutTheChancePercentOfDays()
    {
        const int samples = 20000;
        int found = 0;
        for (int i = 0; i < samples; i++)
        {
            if (PetRules.ForageOf(Guid.NewGuid(), 1 + (i % 365)) != 0)
            {
                found++;
            }
        }

        double percent = 100.0 * found / samples;
        percent.Should().BeInRange(PetRules.ForageChancePercent - 2.0, PetRules.ForageChancePercent + 2.0);
    }

    [Fact]
    public void ForageOf_ChangesFromOneDayToTheNext()
    {
        Guid player = Guid.Parse("00000000-0000-0000-0000-00000000002a");
        int differing = 0;
        for (int day = 1; day < 200; day++)
        {
            if (PetRules.ForageOf(player, day) != PetRules.ForageOf(player, day + 1))
            {
                differing++;
            }
        }

        differing.Should().BeGreaterThan(50, "neighbouring days must not share the same roll");
    }

    [Fact]
    public void ForageItems_AreTheThreeMeadowFoods()
    {
        PetRules.ForageItems.Should().BeEquivalentTo(new[] { Nosum.Gameplay.Items.ItemCatalog.SpiceBerry, Nosum.Gameplay.Items.ItemCatalog.WildPlum, Nosum.Gameplay.Items.ItemCatalog.Mushroom });
    }
}

