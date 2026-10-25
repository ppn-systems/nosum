// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
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
}
