// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Hosting;
using Nalix.Hosting.Protocols;
using Nalix.Runtime.Middleware.Standard;
using Microsoft.EntityFrameworkCore;
using Nosum.Handlers.Account;
using Nosum.Infrastructure.Persistence;

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

        // Persistence: connection string from the environment, migrations applied before any listener opens.
        DbContextOptions<NosumDbContext> database = NosumDatabase.CreateOptions(ResolveConnectionString());
        await NosumDatabase.MigrateAsync(database, shutdown.Token).ConfigureAwait(false);
        HostServices.Register(database);

        NetworkApplication app = BuildApplication(WebSocketPort);
        await using (app.ConfigureAwait(false))
        {
            await app.RunAsync(shutdown.Token).ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>
    /// Reads <c>ConnectionStrings__Default</c>; outside Production a missing value selects the local
    /// development database, in Production it stops the process.
    /// </summary>
    /// <returns>The PostgreSQL connection string.</returns>
    private static string ResolveConnectionString()
    {
        string? environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return NosumDatabase.ResolveConnectionString(
            Environment.GetEnvironmentVariable(NosumDatabase.ConnectionStringVariable),
            string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds the Nalix application: system handlers, game handlers, then the WebSocket listener.
    /// Services that handlers inject (<see cref="HostServices.Register"/>) must be registered first.
    /// </summary>
    /// <param name="webSocketPort">Port the WebSocket listener binds to.</param>
    /// <returns>The built, not yet activated, application.</returns>
    internal static NetworkApplication BuildApplication(ushort webSocketPort)
        => NetworkApplication.CreateBuilder()
            .UseTimeSync()
            .UseSystemControl()
            .MapHandlers(typeof(AccountHandlers))
            .ConfigureDispatchOptions(o =>
            {
                // Fail-closed [PacketPermission] gating and [PacketRateLimit] enforcement.
                _ = o.WithMiddleware(new PermissionMiddleware());
                _ = o.WithMiddleware(new RateLimitMiddleware());
            })
            .MapWebSocket<DefaultProtocol>()
                .OnPort(webSocketPort)
                .WithPath(WebSocketPath)
                .Bind()
            .Build();
}
