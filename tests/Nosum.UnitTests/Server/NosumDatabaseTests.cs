// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.UnitTests.Server;

public sealed class NosumDatabaseTests
{
    [Fact]
    public void ResolveConnectionString_VariableSet_UsesIt()
        => NosumDatabase.ResolveConnectionString("Host=db;Database=x", isProduction: true).Should().Be("Host=db;Database=x");

    [Fact]
    public void ResolveConnectionString_MissingOutsideProduction_UsesDevelopmentDatabase()
        => NosumDatabase.ResolveConnectionString(null, isProduction: false).Should().Be(NosumDatabase.DevelopmentConnectionString);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveConnectionString_MissingInProduction_Throws(string? value)
    {
        Action act = () => NosumDatabase.ResolveConnectionString(value, isProduction: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings__Default*");
    }
}
