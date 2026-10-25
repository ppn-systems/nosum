// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Players;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class ProfanityMaskTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("hello there", "hello there")]
    [InlineData("see you at the market at noon", "see you at the market at noon")]
    public void Mask_CleanText_IsReturnedUnchanged(string? text, string expected)
    {
        ProfanityFilter.Mask(text).Should().Be(expected);
    }

    [Fact]
    public void Mask_CleanText_ComesBackAsTheSameInstance()
    {
        string text = "plant the corn today";

        ProfanityFilter.Mask(text).Should().BeSameAs(text);
    }

    [Theory]
    [InlineData("you fuck", "you ****")]
    [InlineData("FUCK this", "**** this")]
    [InlineData("what the shit is this", "what the **** is this")]
    [InlineData("fuuuck", "******")]
    [InlineData("f.u.c.k", "*.*.*.*")]
    [InlineData("oh, fuck!", "oh, ****!")]
    public void Mask_BlockedWord_HasItsLettersReplacedAndPunctuationKept(string text, string expected)
    {
        ProfanityFilter.Mask(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("đụ má mày", "dumay")]
    [InlineData("Đ.M thằng này", "dm")]
    public void Mask_VietnameseWithAccents_IsRecognised(string text, string term)
    {
        ProfanityFilter.Contains(term).Should().BeTrue("the fixture must name a term the filter knows");

        string masked = ProfanityFilter.Mask(text);

        masked.Should().NotBe(text);
        masked.Should().Contain("*");
    }

    [Theory]
    [InlineData("sh1t happens", "**** happens")]
    [InlineData("f u c k", "* * * *")]
    [InlineData("what a f u c k up", "what * * * * * up")]
    public void Mask_LeetAndSpacedOutLetters_AreMaskedAsOneWord(string text, string expected)
    {
        ProfanityFilter.Mask(text).Should().Be(expected);
    }

    [Fact]
    public void Mask_OnlyTheBlockedWordsAreMasked_NeighboursStay()
    {
        ProfanityFilter.Mask("you are a bitch and I like pie").Should().Be("you are a ***** and I like pie");
    }

    [Theory]
    [InlineData("Scunthorpe is a town")]
    [InlineData("I like sushi and shiitake")]
    [InlineData("the class passed the test")]
    [InlineData("Hancock and Long are names")]
    [InlineData("admin said hello")]
    [InlineData("I will go to the market and sell 10 turnips")]
    public void Mask_InnocentWordsThatContainATerm_AreNotMasked(string text)
    {
        ProfanityFilter.Mask(text).Should().Be(text);
    }

    [Fact]
    public void Mask_OrdinaryShortWordsInASentence_AreNeverGluedIntoAMatch()
    {
        ProfanityFilter.Mask("so I go to a fun day at the big oak").Should().Be("so I go to a fun day at the big oak");
    }

    [Fact]
    public void Mask_KeepsLengthAndPositions()
    {
        string text = "a fuck b shit c";

        string masked = ProfanityFilter.Mask(text);

        masked.Should().HaveLength(text.Length);
        masked.Should().Be("a **** b **** c");
    }
}
