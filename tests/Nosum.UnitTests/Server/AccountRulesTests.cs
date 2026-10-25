// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Abstractions.Accounts;
using Xunit;

namespace Nosum.UnitTests.Server;

public sealed class AccountRulesTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("Farmer_01")]
    [InlineData("a2345678901234567890")]
    public void IsValidUsername_AcceptedForms_ReturnsTrue(string username)
        => AccountRules.IsValidUsername(username).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("a23456789012345678901")]
    [InlineData("1farmer")]
    [InlineData("_farmer")]
    [InlineData("far mer")]
    [InlineData("farmer-1")]
    [InlineData("nông")]
    public void IsValidUsername_RejectedForms_ReturnsFalse(string? username)
        => AccountRules.IsValidUsername(username).Should().BeFalse();

    [Fact]
    public void IsValidPassword_ExactlyMinimumLength_ReturnsTrue()
        => AccountRules.IsValidPassword(new string('x', 8)).Should().BeTrue();

    [Fact]
    public void IsValidPassword_ExactlyMaximumLength_ReturnsTrue()
        => AccountRules.IsValidPassword(new string('x', 128)).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    public void IsValidPassword_TooShort_ReturnsFalse(string? password)
        => AccountRules.IsValidPassword(password).Should().BeFalse();

    [Fact]
    public void IsValidPassword_TooLong_ReturnsFalse()
        => AccountRules.IsValidPassword(new string('x', 129)).Should().BeFalse();

    [Theory]
    [InlineData("vi")]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("zh-Hans-CN")]
    public void IsWellFormedLanguageTag_ValidTags_ReturnsTrue(string tag)
        => AccountRules.IsWellFormedLanguageTag(tag).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("en-")]
    [InlineData("-en")]
    [InlineData("en--US")]
    [InlineData("1n")]
    [InlineData("en_US")]
    [InlineData("englishlanguage")]
    [InlineData("vi-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void IsWellFormedLanguageTag_InvalidTags_ReturnsFalse(string? tag)
        => AccountRules.IsWellFormedLanguageTag(tag).Should().BeFalse();

    [Fact]
    public void NormalizeUsername_MixedCase_IsLowerInvariant()
        => AccountRules.NormalizeUsername("FarmER_1").Should().Be("farmer_1");
}
