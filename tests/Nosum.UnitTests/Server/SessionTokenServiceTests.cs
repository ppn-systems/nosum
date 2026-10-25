// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Abstractions.Security;
using Nosum.Infrastructure.Security;
using Xunit;

namespace Nosum.UnitTests.Server;

public sealed class SessionTokenServiceTests
{
    private readonly SessionTokenService _service = new();

    [Fact]
    public void Create_Token_HasEnoughEntropyAndUrlSafeCharacters()
    {
        IssuedToken issued = _service.Create();

        issued.Token.Length.Should().BeGreaterThanOrEqualTo(43);
        issued.Token.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void Create_TwiceInARow_GivesDifferentTokens()
        => _service.Create().Token.Should().NotBe(_service.Create().Token);

    [Fact]
    public void Create_Hash_EqualsHashOfToken()
    {
        IssuedToken issued = _service.Create();

        _service.Hash(issued.Token).Should().Be(issued.Hash);
    }

    [Fact]
    public void Create_Hash_DoesNotContainToken()
    {
        IssuedToken issued = _service.Create();

        issued.Hash.Should().NotContain(issued.Token);
    }
}
