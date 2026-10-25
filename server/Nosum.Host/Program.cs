// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Hosting;
using Nalix.Hosting.Protocols;

namespace Nosum.Host;

/// <summary>
/// Entry point for the Nosum realtime server. Every client connects over one WebSocket listener.
/// </summary>
internal static class Program
{
    /// <summary>WebSocket port; the client's transport options must use the same port.</summary>
    private const ushort WebSocketPort = 57300;

    /// <summary>WebSocket path; must match the client's <c>WebSocketTransportOptions.Path</c>.</summary>
    internal const string WebSocketPath = "/ws/";

    public static async Task<int> Main()
    {
        using CancellationTokenSource shutdown = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        NetworkApplication app = BuildApplication(WebSocketPort);
        await using (app.ConfigureAwait(false))
        {
            await app.RunAsync(shutdown.Token).ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>
    /// Builds the Nalix application: system handlers, then the WebSocket listener.
    /// </summary>
    /// <param name="webSocketPort">Port the WebSocket listener binds to.</param>
    /// <returns>The built, not yet activated, application.</returns>
    internal static NetworkApplication BuildApplication(ushort webSocketPort)
        => NetworkApplication.CreateBuilder()
            .UseTimeSync()
            .UseSystemControl()
            .MapWebSocket<DefaultProtocol>()
                .OnPort(webSocketPort)
                .WithPath(WebSocketPath)
                .Bind()
            .Build();
}
