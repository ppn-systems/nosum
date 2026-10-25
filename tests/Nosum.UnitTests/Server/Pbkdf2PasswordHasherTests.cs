// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Infrastructure.Security;
using Xunit;

namespace Nosum.UnitTests.Server;

public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 1000);

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        string stored = _hasher.Hash("correct horse");

        _hasher.Verify("correct horse", stored).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        string stored = _hasher.Hash("correct horse");

        _hasher.Verify("wrong horse", stored).Should().BeFalse();
    }

    [Fact]
    public void Hash_SamePasswordTwice_DiffersBecauseOfSalt()
        => _hasher.Hash("same password").Should().NotBe(_hasher.Hash("same password"));

    [Fact]
    public void Hash_Result_NamesAlgorithmAndIterationsAndHidesPassword()
    {
        string stored = _hasher.Hash("hunter2hunter2");

        stored.Should().StartWith("PBKDF2-SHA256$1000$");
        stored.Should().NotContain("hunter2");
    }

    [Fact]
    public void Verify_HashMadeWithOtherIterationCount_StillVerifies()
    {
        string stored = new Pbkdf2PasswordHasher(iterations: 2000).Hash("password-1");

        _hasher.Verify("password-1", stored).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("PBKDF2-SHA256$abc$AAAA$AAAA")]
    [InlineData("PBKDF2-SHA256$1000$not base64!$AAAA")]
    [InlineData("PBKDF2-SHA256$99999999$AAAA$AAAA")]
    [InlineData("OTHER$1000$AAAA$AAAA")]
    public void Verify_UnreadableStoredHash_ReturnsFalse(string stored)
        => _hasher.Verify("anything", stored).Should().BeFalse();

    [Fact]
    public void Constructor_NonPositiveIterations_Throws()
    {
        System.Action act = () => _ = new Pbkdf2PasswordHasher(iterations: 0);

        act.Should().Throw<System.ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DefaultIterations_MeetsOwaspMinimumForPbkdf2Sha256()
        => Pbkdf2PasswordHasher.DefaultIterations.Should().BeGreaterThanOrEqualTo(600_000);
}
