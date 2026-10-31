// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>The guard that maps the Development QA hook (docs/adr/ADR-079): only an explicit Development environment turns it on.</summary>
[Collection(NalixHostSerial.Name)]
public sealed class DevQaGuardTests : IDisposable
{
    private readonly string? _dotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
    private readonly string? _aspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", _dotnet);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _aspnet);
    }

    [Theory]
    [InlineData("Development", null)]
    [InlineData("development", null)]
    [InlineData(null, "Development")]
    [InlineData("Development", "Production")]
    public void IsDevelopment_ExplicitDevelopment_IsOn(string? dotnet, string? aspnet)
    {
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", dotnet);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", aspnet);

        Program.IsDevelopment().Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("Production", null)]
    [InlineData("Staging", "Development")]
    [InlineData("Production", "Development")]
    public void IsDevelopment_AnythingElse_IsOff(string? dotnet, string? aspnet)
    {
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", dotnet);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", aspnet);

        Program.IsDevelopment().Should().BeFalse("an unset or non-Development environment never maps the tester tools");
    }
}
