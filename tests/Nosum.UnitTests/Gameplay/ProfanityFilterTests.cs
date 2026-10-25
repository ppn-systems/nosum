// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Players;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The profanity blocklist (docs/adr/ADR-009 amendments): hits after normalisation, whole-token abbreviations, and names that must pass.</summary>
public sealed class ProfanityFilterTests
{
    [Theory]
    [InlineData("fuck")]
    [InlineData("FUCK")]
    [InlineData("Fu_ck")]
    [InlineData("f u c k")]
    [InlineData("fuuuuck")]
    [InlineData("fvck you")]
    [InlineData("sh1t")]
    [InlineData("5hit")]
    [InlineData("b1tch")]
    [InlineData("b!tch")]
    [InlineData("Big_Shit_77")]
    [InlineData("n1gger")]
    [InlineData("motherfucker")]
    [InlineData("c0cksucker")]
    [InlineData("ditme")]
    [InlineData("Dit me may")]
    [InlineData("DIT_ME")]
    [InlineData("d1tm3")]
    [InlineData("concac")]
    [InlineData("khon nan")]
    [InlineData("Suc Vat")]
    [InlineData("cai lon")]
    [InlineData("hitler")]
    public void Contains_LongTerms_AreFoundAnywhereAfterNormalisation(string name)
        => ProfanityFilter.Contains(name).Should().BeTrue();

    [Theory]
    [InlineData("dm")]
    [InlineData("DM")]
    [InlineData("dmm")]
    [InlineData("dmmmm")]
    [InlineData("dm_99")]
    [InlineData("dm2024")]
    [InlineData("vcl")]
    [InlineData("VCL 123")]
    [InlineData("vcl2024")]
    [InlineData("dcm")]
    [InlineData("clm")]
    [InlineData("cc")]
    [InlineData("c c")]
    [InlineData("d m")]
    [InlineData("cac")]
    [InlineData("lon")]
    [InlineData("L0n")]
    [InlineData("Nam_Lon")]
    [InlineData("buoi")]
    [InlineData("djt")]
    [InlineData("d1t")]
    [InlineData("ass")]
    [InlineData("Big Ass")]
    [InlineData("dick")]
    [InlineData("cock")]
    [InlineData("sex")]
    [InlineData("fuk")]
    public void Contains_ShortAbbreviations_AreFoundAsWholeWords(string name)
        => ProfanityFilter.Contains(name).Should().BeTrue();

    [Theory]
    [InlineData("Thanh")]
    [InlineData("Phuc")]
    [InlineData("Dung")]
    [InlineData("Cuong")]
    [InlineData("Hoang")]
    [InlineData("Long")]
    [InlineData("Diem")]
    [InlineData("Nguyen")]
    [InlineData("Nguyen Thang")]
    [InlineData("Thang Nguyen")]
    [InlineData("Ngoc Chau")]
    [InlineData("Duc Hai")]
    [InlineData("Minh Duc")]
    [InlineData("Admin")]
    [InlineData("Admiral")]
    [InlineData("Dmitri")]
    [InlineData("Duman")]
    [InlineData("Duong")]
    [InlineData("Bui")]
    [InlineData("Scunthorpe")]
    [InlineData("Scunth0rpe")]
    [InlineData("class")]
    [InlineData("Classic")]
    [InlineData("Assam")]
    [InlineData("Bassist")]
    [InlineData("Hancock")]
    [InlineData("Peacock")]
    [InlineData("Dickens")]
    [InlineData("Essex")]
    [InlineData("Sussex")]
    [InlineData("Analyst")]
    [InlineData("Grape")]
    [InlineData("Raccoon")]
    [InlineData("Cumberland")]
    [InlineData("Atwater")]
    [InlineData("Hoe")]
    [InlineData("Farmer01")]
    [InlineData("Shiitake")]
    [InlineData("Mississippi")]
    [InlineData("Pass")]
    [InlineData("Cassie")]
    public void Contains_OrdinaryNamesAndWords_AreClean(string name)
        => ProfanityFilter.Contains(name).Should().BeFalse();

    [Fact]
    public void Contains_VietnameseNames_AnyFamilyGivenPairing_IsClean()
    {
        string[] family = ["Nguyen", "Tran", "Le", "Pham", "Hoang", "Huynh", "Phan", "Vu", "Vo", "Dang", "Bui", "Do", "Ho", "Ngo", "Duong", "Ly", "Dinh", "Trinh", "Lam", "Mai"];
        string[] middle = ["Van", "Thi", "Duc", "Minh", "Anh", "Ngoc", "Quang", "Huu", "Thanh", "Hong", "Kim", "Gia", "Bao", "Xuan", "Tuan"];
        string[] given =
        [
            "An", "Anh", "Bach", "Binh", "Cuong", "Chau", "Chi", "Cong", "Dat", "Diem", "Dung", "Duy", "Dai", "Dan", "Dao", "Giang", "Hai", "Hanh", "Hieu", "Hoa",
            "Hoang", "Hung", "Huong", "Khanh", "Khoa", "Kien", "Lan", "Linh", "Loc", "Long", "Luan", "Mai", "Manh", "My", "Nam", "Nga", "Nghia", "Ngan", "Nhan",
            "Nhi", "Phat", "Phong", "Phuc", "Phuong", "Quan", "Quyen", "Quynh", "Son", "Tai", "Tam", "Thang", "Thao", "Thinh", "Thu", "Thuy", "Tien", "Trang",
            "Trung", "Tu", "Tuan", "Tuyet", "Uyen", "Van", "Viet", "Vinh", "Vy", "Xuan", "Yen", "Cam", "Sang", "Sinh", "Ty", "Ut", "Cao",
        ];

        string[] offenders = [.. (from f in family from m in middle from g in given select $"{f} {m} {g}")
            .SelectMany(n => new[] { n, n.Replace(" ", string.Empty, System.StringComparison.Ordinal), n.Replace(' ', '_') })
            .Where(ProfanityFilter.Contains)];

        offenders.Should().BeEmpty("no ordinary Vietnamese name, spelled in any separator style, may trip the blocklist");
    }

    [Theory]
    [InlineData("Lon")]
    [InlineData("Nguyen Dit")]
    public void Contains_ShortWordsThatAreAlsoSyllables_AreBlockedOnlyWhenTheyStandAlone(string name)
        => ProfanityFilter.Contains(name).Should().BeTrue("a lone \"Lon\" or \"Dit\" is the profanity");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Contains_NothingToCheck_IsClean(string? text)
        => ProfanityFilter.Contains(text).Should().BeFalse();

    [Fact]
    public void Contains_AccentedTextIsReadWithoutAccents()
    {
        ProfanityFilter.Contains("Đụ mẹ").Should().BeTrue();
        ProfanityFilter.Contains("Đụ").Should().BeFalse("\"Du\" alone is a common given name");
        ProfanityFilter.Contains("Địt mẹ mày").Should().BeTrue();
        ProfanityFilter.Contains("Khốn nạn").Should().BeTrue();
        ProfanityFilter.Contains("Thanh Phúc").Should().BeFalse();
    }

    [Fact]
    public void Lists_AreCleanData_LowerCaseAsciiWithoutSeparatorsOrDigits()
    {
        foreach (string term in ProfanityTerms.Substrings.Concat(ProfanityTerms.Tokens).Concat(ProfanityTerms.Innocent))
        {
            term.Should().MatchRegex("^[a-z]+$", $"'{term}' must already be in the filter's normal form");
        }
    }
}
