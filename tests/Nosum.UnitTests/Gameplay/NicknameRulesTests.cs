// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Text;
using FluentAssertions;
using Nosum.Gameplay.Players;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The nickname rules (docs/adr/ADR-009 amendments): length, letters with Vietnamese diacritics, separators, reserved names, NFC and uniqueness keys.</summary>
public sealed class NicknameRulesTests
{
    [Theory]
    [InlineData("Minh")]
    [InlineData("Nguyễn Văn_An")]
    [InlineData("Đặng Thị Hồng")]
    [InlineData("Ông Già 99")]
    [InlineData("ABC")]
    [InlineData("a1b")]
    [InlineData("Lê Hoàng Phúc_7")]
    [InlineData("  padded  ")]
    [InlineData("Sixteen_chars_xx")]
    public void Validate_AcceptableNames_AreOk(string name)
        => NicknameRules.Validate(name).Should().Be(NicknameVerdict.Ok);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("Seventeen_chars_xx")]
    [InlineData("12345")]
    [InlineData("has-hyphen")]
    [InlineData("two  spaces")]
    [InlineData("under__score")]
    [InlineData("mix _ed")]
    [InlineData("_leading")]
    [InlineData("trailing_")]
    [InlineData("bad!")]
    [InlineData("tab\tname")]
    [InlineData("emoji😀x")]
    [InlineData("guest-1234")]
    public void Validate_BreakingARule_IsInvalid(string name)
        => NicknameRules.Validate(name).Should().Be(NicknameVerdict.Invalid);

    [Fact]
    public void Validate_Null_IsInvalid() => NicknameRules.Validate(null).Should().Be(NicknameVerdict.Invalid);

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Admin_2")]
    [InlineData("Ádmin")]
    [InlineData("Moderator")]
    [InlineData("System")]
    [InlineData("Quản Trị")]
    [InlineData("Hệ Thống")]
    [InlineData("Khách")]
    [InlineData("Khách 1234")]
    [InlineData("Guest 1234")]
    [InlineData("nosum")]
    public void Validate_ReservedOrLookAlike_IsReserved(string name)
        => NicknameRules.Validate(name).Should().Be(NicknameVerdict.Reserved);

    [Fact]
    public void Validate_NameThatMerelyContainsAReservedWord_IsOk()
        => NicknameRules.Validate("Adminton").Should().Be(NicknameVerdict.Ok);

    [Fact]
    public void Validate_DecomposedVietnamese_IsAcceptedAsItsComposedForm()
    {
        string composed = "Nguyễn Hoàng";
        string decomposed = composed.Normalize(NormalizationForm.FormD);
        decomposed.Should().NotBe(composed);

        NicknameRules.Validate(decomposed).Should().Be(NicknameVerdict.Ok);
        NicknameRules.Normalize(decomposed).Should().Be(composed);
        NicknameRules.Key(decomposed).Should().Be(NicknameRules.Key(composed));
    }

    [Fact]
    public void Key_IsCaseInsensitiveAndReadsUnderscoreAsSpace()
    {
        NicknameRules.Key("Minh_Anh").Should().Be(NicknameRules.Key("minh anh")).And.Be(NicknameRules.Key("MINH ANH"));
        NicknameRules.Key("Đức").Should().Be(NicknameRules.Key("đức"));
        NicknameRules.Key("Minh").Should().NotBe(NicknameRules.Key("Mình"), "different letters are different names");
    }

    [Fact]
    public void GuestName_IsNeutralAndRoundTrips()
    {
        string name = NicknameRules.GuestName(42);

        name.Should().Be("guest-0042");
        NicknameRules.IsGuestName(name, out string number).Should().BeTrue();
        number.Should().Be("0042");
        NicknameRules.GuestName(1234567).Should().Be("guest-1234567");
    }

    [Theory]
    [InlineData("guest-")]
    [InlineData("guest-12a")]
    [InlineData("Guest-1234")]
    [InlineData("guest 1234")]
    [InlineData("Minh")]
    [InlineData("")]
    [InlineData(null)]
    public void IsGuestName_AnythingElse_IsFalse(string? name)
        => NicknameRules.IsGuestName(name, out _).Should().BeFalse();
}
