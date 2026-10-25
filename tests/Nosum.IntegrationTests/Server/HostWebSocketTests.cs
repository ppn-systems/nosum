// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Hosting;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

[Collection(NalixHostSerial.Name)]
public sealed class HostWebSocketTests
{
    [Fact]
    public async Task BuildApplication_Activated_AcceptsWebSocketUpgradeOnPath()
    {
        ushort port = GetFreePort();
        await using NetworkApplication app = Program.BuildApplication(port);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        await app.ActivateAsync(timeout.Token);
        try
        {
            using ClientWebSocket client = new();
            await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}{Program.WebSocketPath}"), timeout.Token);

            client.State.Should().Be(WebSocketState.Open);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
