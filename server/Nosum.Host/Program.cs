// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.Hosting.Protocols;
using Nalix.Runtime.Groups;
using Nalix.Runtime.Middleware.Standard;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Maps;
using Nosum.Handlers.Account;
using Nosum.Handlers.Player;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Maps;
using Nosum.Infrastructure.Players;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Presence;

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

    /// <summary>Id of the farm map, where a new player starts.</summary>
    internal const string FarmMapId = GameMaps.DefaultId;

    /// <summary>Time between map snapshots (10 per second, docs/adr/ADR-008).</summary>
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(100);

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

        // Idle guests (30 days without coming back) are deleted once an hour.
        Task guestCleanup = new GuestCleanupService(database, TimeProvider.System).RunAsync(
            GuestCleanupService.DefaultInterval,
            ex => Console.Error.WriteLine("Guest cleanup failed: " + ex.Message),
            shutdown.Token);

        NetworkApplication app = BuildApplication(WebSocketPort);
        await using (app.ConfigureAwait(false))
        {
            await app.RunAsync(shutdown.Token).ConfigureAwait(false);
        }

        await guestCleanup.ConfigureAwait(false);

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
    /// Builds the Nalix application: services, system handlers, game handlers, then the WebSocket
    /// listener. Account services (<see cref="HostServices.Register"/>) must be registered first.
    /// </summary>
    /// <param name="webSocketPort">Port the WebSocket listener binds to.</param>
    /// <returns>The built, not yet activated, application.</returns>
    internal static NetworkApplication BuildApplication(ushort webSocketPort)
    {
        RegisterServices();

        return NetworkApplication.CreateBuilder()
            .UseGroups()
            .UseTimeSync()
            .UseSystemControl()
            .UseSecureConnections()
            .MapHandlers(typeof(PlayerHandlers))
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

    // Services are registered before the builder so [Inject] fields of the handlers resolve them.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The player service lives as long as the process; its snapshot loop ends by itself when the last player leaves.")]
    private static void RegisterServices()
    {
        IConnectionGroupRegistry groups = new InMemoryGroupStore();
        InstanceManager.Instance.Register(groups);

        MapPresence presence = new(groups);
        TmxMapCatalog maps = new(GameMaps.All, Path.Combine(AppContext.BaseDirectory, "maps"));
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()
            ?? throw new InvalidOperationException("HostServices.Register must run before BuildApplication.");
        PlayerService players = new(presence, maps, states, TimeProvider.System, SnapshotInterval);

        InstanceManager.Instance.Register<IMapPresence>(presence);
        InstanceManager.Instance.Register<IMapCatalog>(maps);
        InstanceManager.Instance.Register<IPlayerService>(players);
        InstanceManager.Instance.Register<ISessionRegistry>(new SessionRegistry(presence));
    }
}
