// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Contracts.Common;
using Xunit;

namespace Nosum.UnitTests.Contracts;

public sealed class GameLanguagesTests
{
    [Theory]
    [InlineData(GameLanguage.Unspecified, "vi")]
    [InlineData(GameLanguage.Vietnamese, "vi")]
    [InlineData(GameLanguage.English, "en")]
    public void ToTag_DefinedLanguage_ReturnsBcp47Tag(GameLanguage language, string expected)
        => GameLanguages.ToTag(language).Should().Be(expected);

    [Theory]
    [InlineData("vi", GameLanguage.Vietnamese)]
    [InlineData("vi-VN", GameLanguage.Vietnamese)]
    [InlineData("EN", GameLanguage.English)]
    [InlineData("en-US", GameLanguage.English)]
    [InlineData("fr", GameLanguage.Unspecified)]
    [InlineData("", GameLanguage.Unspecified)]
    [InlineData(null, GameLanguage.Unspecified)]
    public void FromTag_Tag_ReturnsLanguageByPrimarySubtag(string? tag, GameLanguage expected)
        => GameLanguages.FromTag(tag).Should().Be(expected);

    [Theory]
    [InlineData(GameLanguage.Unspecified, true)]
    [InlineData(GameLanguage.English, true)]
    [InlineData((GameLanguage)3, false)]
    [InlineData((GameLanguage)255, false)]
    public void IsDefined_Value_AcceptsOnlyDefinedLanguages(GameLanguage language, bool expected)
        => GameLanguages.IsDefined(language).Should().Be(expected);

    [Fact]
    public void Resolve_Unspecified_IsVietnamese()
        => GameLanguages.Resolve(GameLanguage.Unspecified).Should().Be(GameLanguage.Vietnamese);

    [Fact]
    public void Values_AreAppendOnly_NumbersNeverChange()
    {
        ((byte)GameLanguage.Unspecified).Should().Be(0);
        ((byte)GameLanguage.Vietnamese).Should().Be(1);
        ((byte)GameLanguage.English).Should().Be(2);
    }
}
