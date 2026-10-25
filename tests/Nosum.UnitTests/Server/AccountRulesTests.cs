// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Abstractions.Accounts;
using Xunit;

namespace Nosum.UnitTests.Server;

public sealed class AccountRulesTests
{
    [Theory]
    [InlineData("farmer01@gmail.com")]
    [InlineData("Farmer.One@Gmail.COM")]
    [InlineData("a2345678901234567890123456789a@gmail.com")]
    [InlineData("abcdef@gmail.com")]
    public void IsValidEmail_GmailAddresses_ReturnsTrue(string email)
        => AccountRules.IsValidEmail(email).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("farmer01")]
    [InlineData("farmer01@yahoo.com")]
    [InlineData("farmer01@gmail.com.vn")]
    [InlineData("farmer01@mail.gmail.com")]
    [InlineData("farmer01@googlemail.com")]
    [InlineData("abcde@gmail.com")]
    [InlineData("a23456789012345678901234567890a@gmail.com")]
    [InlineData(".farmer01@gmail.com")]
    [InlineData("farmer01.@gmail.com")]
    [InlineData("farmer..01@gmail.com")]
    [InlineData("farmer+tag@gmail.com")]
    [InlineData("far mer01@gmail.com")]
    [InlineData("farmer_01@gmail.com")]
    [InlineData("n\u00f4ngdan@gmail.com")]
    [InlineData("a@b@gmail.com")]
    [InlineData("@gmail.com")]
    public void IsValidEmail_OtherAddresses_ReturnsFalse(string? email)
        => AccountRules.IsValidEmail(email).Should().BeFalse();

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

    [Fact]
    public void NormalizeEmail_MixedCase_IsLowerInvariant()
        => AccountRules.NormalizeEmail("FarmER.One@GMAIL.com").Should().Be("farmer.one@gmail.com");
}
