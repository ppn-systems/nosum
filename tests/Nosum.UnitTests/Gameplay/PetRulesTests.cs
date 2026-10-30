// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Pets;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class PetRulesTests
{
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
