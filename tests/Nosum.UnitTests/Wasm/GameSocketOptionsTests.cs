// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Nosum.Wasm.Socket;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class GameSocketOptionsTests
{
    [Fact]
    public void ForPageHost_HttpsPage_UsesTheProxyOnThePagePort()
    {
        GameSocketOptions options = GameSocketOptions.ForPageHost(new Uri("https://play.example.org/"));

        options.Host.Should().Be("play.example.org");
        options.UseTls.Should().BeTrue();
        options.Port.Should().Be(443);
        options.Path.Should().Be("/ws/");
    }

    [Fact]
    public void ForPageHost_HttpsPageOnAnotherPort_FollowsThatPort()
        => GameSocketOptions.ForPageHost(new Uri("https://play.example.org:8443/")).Port.Should().Be(8443);

    [Fact]
    public void ForPageHost_HttpPage_IsADevServerAndGoesStraightToTheGamePort()
    {
        GameSocketOptions options = GameSocketOptions.ForPageHost(new Uri("http://localhost:5000/"));

        options.UseTls.Should().BeFalse();
        options.Port.Should().Be(GameSocketOptions.DefaultPort);
    }

    [Fact]
    public void ApplyBootConfiguration_ServerUrl_ReplacesTheAddressThePageGave()
    {
        Dictionary<string, string?> boot = new() { ["Server:Url"] = "wss://nosum.ppnsys.com/ws/" };

        GameSocketOptions options = GameSocketOptions.ForPageHost(new Uri("https://nosum.pages.dev/"))
            .ApplyBootConfiguration(key => boot.GetValueOrDefault(key));

        options.Host.Should().Be("nosum.ppnsys.com");
        options.Port.Should().Be(443);
        options.UseTls.Should().BeTrue();
        options.Path.Should().Be("/ws/");
    }

    [Fact]
    public void ApplyBootConfiguration_NoServerUrl_KeepsTheAddressOfThePage()
    {
        GameSocketOptions options = GameSocketOptions.ForPageHost(new Uri("https://play.example.org/"))
            .ApplyBootConfiguration(_ => null);

        options.Host.Should().Be("play.example.org");
        options.Port.Should().Be(443);
    }

    [Theory]
    [InlineData("https://nosum.ppnsys.com/ws/")]
    [InlineData("nosum.ppnsys.com/ws/")]
    [InlineData("wss://")]
    public void ApplyBootConfiguration_ServerUrlNotAWebSocketAddress_IsRefused(string url)
    {
        Dictionary<string, string?> boot = new() { ["Server:Url"] = url };

        Action apply = () => GameSocketOptions.ForPageHost(new Uri("https://nosum.pages.dev/")).ApplyBootConfiguration(key => boot.GetValueOrDefault(key));

        apply.Should().Throw<FormatException>().WithMessage("*Server:Url*");
    }

    [Fact]
    public void KeepAliveInterval_Default_IsWellUnderTheProxyIdleTimeout()
        => new GameSocketOptions().KeepAliveInterval.Should().BeLessThan(TimeSpan.FromSeconds(100));
}
