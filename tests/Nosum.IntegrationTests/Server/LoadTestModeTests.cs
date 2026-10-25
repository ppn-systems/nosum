// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>The switch that turns the rate limits off for a load test (docs/adr/ADR-031 D8) can never do so in production.</summary>
[Collection(NalixHostSerial.Name)]
public sealed class LoadTestModeTests : IDisposable
{
    private readonly string? _load = Environment.GetEnvironmentVariable("NOSUM_LOAD_TEST");
    private readonly string? _dotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
    private readonly string? _aspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("NOSUM_LOAD_TEST", _load);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", _dotnet);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _aspnet);
    }

    [Theory]
    [InlineData(null, "Production")]
    [InlineData(null, null)]
    [InlineData("0", "Staging")]
    [InlineData("true", "Staging")]
    public void LoadTestMode_VariableNotOne_IsOffWhateverTheEnvironment(string? load, string? environment)
    {
        Set(load, environment);

        Program.LoadTestMode().Should().BeFalse();
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("staging")]
    [InlineData("Development")]
    public void LoadTestMode_OneInStagingOrDevelopment_IsOn(string environment)
    {
        Set("1", environment);

        Program.LoadTestMode().Should().BeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData(null)]
    [InlineData("Testing")]
    public void LoadTestMode_OneAnywhereElse_RefusesInsteadOfSilentlyTurningLimitsOff(string? environment)
    {
        Set("1", environment);

        Action act = () => Program.LoadTestMode();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Staging or Development*");
    }

    private static void Set(string? load, string? environment)
    {
        Environment.SetEnvironmentVariable("NOSUM_LOAD_TEST", load);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", environment);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }
}
