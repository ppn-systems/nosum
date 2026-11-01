// Copyright (c) 2026 PPN Systems. All rights reserved.

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
}
