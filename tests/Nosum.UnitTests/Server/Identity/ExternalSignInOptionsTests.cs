// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Infrastructure.Identity;
using Xunit;

namespace Nosum.UnitTests.Server.Identity;

/// <summary>The sign-in settings: the tenant decides which key set and issuer the server trusts, so a bad value stops the host at start-up.</summary>
public sealed class ExternalSignInOptionsTests
{
    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("consumers")]
    [InlineData("9b0c3c9e-2d1f-4a6b-8c7d-1e2f3a4b5c6d")]
    public void Validate_KnownTenant_Passes(string tenant)
    {
        Action validate = () => new ExternalSignInOptions { MicrosoftTenant = tenant }.Validate();

        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData("contoso.onmicrosoft.com")]
    [InlineData("common/../evil")]
    [InlineData("")]
    public void Validate_TenantThatIsNotAnAliasOrAGuid_Throws(string tenant)
    {
        Action validate = () => new ExternalSignInOptions { MicrosoftTenant = tenant }.Validate();

        validate.Should().Throw<InvalidOperationException>().WithMessage("*NOSUM_MICROSOFT_TENANT*");
    }

    [Fact]
    public void Defaults_AreOffForBothProvidersAndCommonTenant()
    {
        ExternalSignInOptions options = new();

        options.GoogleClientId.Should().BeEmpty();
        options.MicrosoftClientId.Should().BeEmpty();
        options.MicrosoftTenant.Should().Be("common");
    }
}
