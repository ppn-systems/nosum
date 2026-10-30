// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.Hosting.Protocols;
using Nalix.Network.Options;
using Nalix.Runtime.Groups;
using Nalix.Runtime.Middleware.Standard;
using Nosum.Abstractions.Observability;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Maps;
using Nosum.Handlers.Account;
using Nosum.Handlers.Admin;
using Nosum.Handlers.Blacksmith;
using Nosum.Handlers.Building;
using Nosum.Handlers.Calendar;
using Nosum.Handlers.Chat;
using Nosum.Handlers.Combat;
using Nosum.Handlers.Coop;
using Nosum.Handlers.Dev;
using Nosum.Handlers.Farm;
using Nosum.Handlers.Fishing;
using Nosum.Handlers.Friends;
using Nosum.Handlers.Goals;
using Nosum.Handlers.GroundItem;
using Nosum.Handlers.Inventory;
using Nosum.Handlers.Journal;
using Nosum.Handlers.Look;
using Nosum.Handlers.Mail;
using Nosum.Handlers.Market;
using Nosum.Handlers.Mine;
using Nosum.Handlers.Node;
using Nosum.Handlers.Placeable;
using Nosum.Handlers.Player;
using Nosum.Handlers.Quest;
using Nosum.Handlers.Requests;
using Nosum.Handlers.Security;
using Nosum.Handlers.Shop;
using Nosum.Handlers.Trade;
using Nosum.Handlers.Visit;
using Nosum.Host.Ops;
using Nosum.Infrastructure.Persistence;

namespace Nosum.Host;

/// <summary>
/// Entry point for the Nosum realtime server. Every client connects over one WebSocket listener.
/// </summary>
internal static partial class Program
{
    /// <summary>WebSocket port; the client's transport options must use the same port.</summary>
    private const ushort WebSocketPort = 57300;

    /// <summary>WebSocket path; must match the client's <c>WebSocketTransportOptions.Path</c>.</summary>
    internal const string WebSocketPath = "/ws/";

    /// <summary>Id of the farm map, where a new player starts.</summary>
    internal const string FarmMapId = GameMaps.DefaultId;

    /// <summary>Time between map snapshots (10 per second, docs/adr/ADR-008).</summary>
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(100);

    public static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource shutdown = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        // Command line (admin role, container healthcheck): run, print, exit - nothing else of the server starts. Only the admin commands need the database.
        if (args.Length > 0)
        {
            return await HostCommands.RunAsync(args, () => NosumDatabase.CreateOptions(ResolveConnectionString()), OpsPort(), Console.Out, shutdown.Token).ConfigureAwait(false);
        }

        // Persistence: connection string from the environment, migrations applied before any listener opens.
        DbContextOptions<NosumDbContext> database = NosumDatabase.CreateOptions(ResolveConnectionString());

        // Operations (docs/adr/ADR-031): JSON logs on stdout, and /healthz /readyz /metrics up before the migration, so a slow one shows as not ready.
        OpsLog.Configure(Console.Out);
        OpsLog.RouteConsoleError();
        OpsEndpoint ops = OpsEndpoint.Start(Environment.GetEnvironmentVariable("NOSUM_OPS_HOST") ?? "localhost", OpsPort(), ct => NosumDatabase.IsReadyAsync(database, ct));
        await using (ops.ConfigureAwait(false))
        {
            return await RunServerAsync(database, shutdown).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Tells whether the per-address handler rate limits are off for a load test (docs/adr/ADR-031 D8): the bots of <c>bench/Nosum.Bench.Load</c> share one
    /// address, so the limits would throttle them as one player. Needs <c>NOSUM_LOAD_TEST=1</c> and an explicit <c>Staging</c> or <c>Development</c>
    /// environment; with the variable set anywhere else the host refuses to start, so a forgotten variable can never switch the limits off in production.
    /// </summary>
    /// <returns><see langword="true"/> when the rate limits are off.</returns>
    /// <exception cref="InvalidOperationException">The variable is set outside Staging and Development.</exception>
    internal static bool LoadTestMode()
    {
        if (Environment.GetEnvironmentVariable("NOSUM_LOAD_TEST") != "1")
        {
            return false;
        }

        string? environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (!string.Equals(environment, "Staging", StringComparison.OrdinalIgnoreCase) && !string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("NOSUM_LOAD_TEST=1 turns the rate limits off and is only honoured when DOTNET_ENVIRONMENT is Staging or Development. Unset it.");
        }

        return true;
    }

    /// <summary>Reads <c>NOSUM_WS_PORT</c>, the port of the WebSocket listener; <see cref="WebSocketPort"/> when unset.</summary>
    /// <returns>The port.</returns>
    private static ushort WsPort()
        => ushort.TryParse(Environment.GetEnvironmentVariable("NOSUM_WS_PORT"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ushort port) ? port : WebSocketPort;

    /// <summary>Reads <c>NOSUM_OPS_PORT</c>, the port of the health and metrics endpoint; <see cref="OpsEndpoint.DefaultPort"/> when unset.</summary>
    /// <returns>The port.</returns>
    private static ushort OpsPort()
        => ushort.TryParse(Environment.GetEnvironmentVariable("NOSUM_OPS_PORT"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ushort port) ? port : OpsEndpoint.DefaultPort;

    /// <summary>
    /// Tells whether the host was started in the Development environment: only an explicit <c>DOTNET_ENVIRONMENT</c> or
    /// <c>ASPNETCORE_ENVIRONMENT</c> of <c>Development</c> counts; an unset variable is not development (tester tools stay off).
    /// </summary>
    /// <returns><see langword="true"/> in Development.</returns>
    internal static bool IsDevelopment()
    {
        string? environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase);
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
    /// <param name="clock">The clock crops grow by and quests and audit entries are stamped with; tests pass a fake one. Defaults to the system clock.</param>
    /// <param name="development">Whether the host runs in the Development environment. Only then are the tester tools (<see cref="DevHandlers"/>) registered; Production never maps them.</param>
    /// <returns>The built, not yet activated, application.</returns>
    internal static NetworkApplication BuildApplication(ushort webSocketPort, TimeProvider? clock = null, bool development = false)
    {
        RegisterServices(clock ?? TimeProvider.System, development);

        INetworkApplicationBuilder builder = NetworkApplication.CreateBuilder()
            .UseGroups()
            .UseTimeSync()
            .UseSystemControl()
            .UseSecureConnections(ServerKey.ConfiguredPath())
            .Configure<ConnectionQuotaOptions>(AbuseLimits.Apply)
            .MapHandlers(typeof(PlayerHandlers))
            .MapHandlers(typeof(AccountHandlers))
            .MapHandlers(typeof(InventoryHandlers))
            .MapHandlers(typeof(FarmHandlers))
            .MapHandlers(typeof(QuestHandlers))
            .MapHandlers(typeof(MarketHandlers))
            .MapHandlers(typeof(LookHandlers))
            .MapHandlers(typeof(TradeHandlers))
            .MapHandlers(typeof(CoopHandlers))
            .MapHandlers(typeof(PlaceableHandlers))
            .MapHandlers(typeof(CraftingHandlers))
            .MapHandlers(typeof(ChestHandlers))
            .MapHandlers(typeof(FurnaceHandlers))
            .MapHandlers(typeof(BuildingHandlers))
            .MapHandlers(typeof(NodeHandlers))
            .MapHandlers(typeof(FishingHandlers))
            .MapHandlers(typeof(VisitHandlers))
            .MapHandlers(typeof(ShopHandlers))
            .MapHandlers(typeof(BlacksmithHandlers))
            .MapHandlers(typeof(MineHandlers))
            .MapHandlers(typeof(CombatHandlers))
            .MapHandlers(typeof(BestiaryHandlers))
            .MapHandlers(typeof(JournalHandlers))
            .MapHandlers(typeof(GoalHandlers))
            .MapHandlers(typeof(CalendarHandlers))
            .MapHandlers(typeof(GroundItemHandlers))
            .MapHandlers(typeof(ChatHandlers))
            .MapHandlers(typeof(FriendHandlers))
            .MapHandlers(typeof(MailHandlers))
            .MapHandlers(typeof(AdminHandlers))
            .MapHandlers(typeof(RequestHandlers));

        // Tester tools exist only in Development: no handler, no service, no opcode in Production.
        if (development)
        {
            builder = builder.MapHandlers(typeof(DevHandlers));
        }

        return builder
            .ConfigureDispatchOptions(o =>
            {
                // Handler timing first (it includes the checks below), then declared input bounds (docs/adr/ADR-032 D3), then fail-closed [PacketPermission] gating and [PacketRateLimit] enforcement.
                _ = o.WithMiddleware(new MetricsMiddleware(static () => InstanceManager.Instance.GetExistingInstance<IMapPresence>()));
                _ = o.WithMiddleware(new InputBoundsMiddleware());
                _ = o.WithMiddleware(new PermissionMiddleware());
                if (!LoadTestMode())
                {
                    _ = o.WithMiddleware(new RateLimitMiddleware());
                }
            })
            .MapWebSocket<DefaultProtocol>()
                .OnPort(webSocketPort)
                .WithPath(WebSocketPath)
                .Bind()
            .Build();
    }
}
