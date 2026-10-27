// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Text;
using FluentAssertions;
using Nosum.Gameplay.Players;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The nickname rules (docs/adr/ADR-009 amendments): length, unaccented ASCII letters and digits, separators, reserved names, profanity and uniqueness keys.</summary>
public sealed class NicknameRulesTests
{
    [Theory]
    [InlineData("Minh")]
    [InlineData("Nguyen Van_An")]
    [InlineData("Dang Thi Hong")]
    [InlineData("Ong Gia 99")]
    [InlineData("ABC")]
    [InlineData("a1b")]
    [InlineData("Le Hoang Phuc_7")]
    [InlineData("Thanh")]
    [InlineData("Phuc")]
    [InlineData("Dung")]
    [InlineData("Cuong")]
    [InlineData("Scunthorpe")]
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
    [InlineData("Mình")]
    [InlineData("Nguyễn")]
    [InlineData("Đức Anh")]
    [InlineData("Minh\u0300")]
    [InlineData("Ádmin")]
    [InlineData("Иван")]
    [InlineData("Αλφα")]
    [InlineData("Аdmin")]
    [InlineData("ＡＢＣＤ")]
    [InlineData("a\u200bb\u200bc")]
    [InlineData("Na\u00a0me")]
    [InlineData("محمد علي")]
    [InlineData("日本語の名前")]
    [InlineData("name\nline")]
    [InlineData("x\0yz1")]
    [InlineData("\u202eGnimoc")]
    public void Validate_BreakingARule_IsInvalid(string name)
        => NicknameRules.Validate(name).Should().Be(NicknameVerdict.Invalid);

    [Fact]
    public void Validate_VeryLongNames_AreInvalid()
    {
        NicknameRules.Validate(new string('a', 200)).Should().Be(NicknameVerdict.Invalid);
        NicknameRules.Validate(new string('a', 10_000)).Should().Be(NicknameVerdict.Invalid);
    }

    [Fact]
    public void Validate_Null_IsInvalid() => NicknameRules.Validate(null).Should().Be(NicknameVerdict.Invalid);

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Admin_2")]
    [InlineData("Moderator")]
    [InlineData("System")]
    [InlineData("Quan Tri")]
    [InlineData("He Thong")]
    [InlineData("Khach")]
    [InlineData("Khach 1234")]
    [InlineData("Guest 1234")]
    [InlineData("nosum")]
    public void Validate_ReservedOrLookAlike_IsReserved(string name)
        => NicknameRules.Validate(name).Should().Be(NicknameVerdict.Reserved);

    [Fact]
    public void Validate_NameThatMerelyContainsAReservedWord_IsOk()
        => NicknameRules.Validate("Adminton").Should().Be(NicknameVerdict.Ok);

    [Fact]
    public void Validate_DecomposedAccentedText_IsRefusedLikeItsComposedForm()
    {
        string composed = "Nguyễn Hoàng";
        string decomposed = composed.Normalize(NormalizationForm.FormD);
        decomposed.Should().NotBe(composed);

        NicknameRules.Validate(composed).Should().Be(NicknameVerdict.Invalid);
        NicknameRules.Validate(decomposed).Should().Be(NicknameVerdict.Invalid);
    }

    [Theory]
    [InlineData("Fuck")]
    [InlineData("dm_1")]
    [InlineData("VCL_99")]
    [InlineData("Ditme")]
    [InlineData("sh1t")]
    [InlineData("big ass")]
    public void Validate_Profanity_IsProfane(string name)
        => NicknameRules.Validate(name).Should().Be(NicknameVerdict.Profane);

    [Fact]
    public void Validate_ShapeIsCheckedBeforeProfanity_AndReservedBeforeProfanity()
    {
        NicknameRules.Validate("fuck!").Should().Be(NicknameVerdict.Invalid);
        NicknameRules.Validate("fück").Should().Be(NicknameVerdict.Invalid);
        NicknameRules.Validate("admin").Should().Be(NicknameVerdict.Reserved);
    }

    [Fact]
    public void Key_IsCaseInsensitiveAndReadsUnderscoreAsSpace()
    {
        NicknameRules.Key("Minh_Anh").Should().Be(NicknameRules.Key("minh anh")).And.Be(NicknameRules.Key("MINH ANH"));
        NicknameRules.Key("Minh").Should().NotBe(NicknameRules.Key("Mình"), "a stored accented name and a new unaccented one are different names");
        NicknameRules.Key("Mình").Should().Be(NicknameRules.Key("MÌNH"), "stored legacy names keep working as keys");
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
