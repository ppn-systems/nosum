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
using Nosum.Abstractions.Calendar;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Look;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Market;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Shops;
using Nosum.Abstractions.Quests;
using Nosum.Abstractions.Trade;
using Nosum.Abstractions.Visits;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Maps;
using Nosum.Handlers.Account;
using Nosum.Handlers.Calendar;
using Nosum.Handlers.Coop;
using Nosum.Handlers.Dev;
using Nosum.Handlers.Farm;
using Nosum.Handlers.Inventory;
using Nosum.Handlers.Look;
using Nosum.Handlers.Market;
using Nosum.Handlers.Placeable;
using Nosum.Handlers.Player;
using Nosum.Handlers.Quest;
using Nosum.Handlers.Shop;
using Nosum.Handlers.Trade;
using Nosum.Handlers.Visit;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Calendar;
using Nosum.Infrastructure.Coops;
using Nosum.Infrastructure.Dev;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Farm;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Looks;
using Nosum.Infrastructure.Maps;
using Nosum.Infrastructure.Market;
using Nosum.Infrastructure.Placeables;
using Nosum.Infrastructure.Players;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Presence;
using Nosum.Infrastructure.Quests;
using Nosum.Infrastructure.Shops;
using Nosum.Infrastructure.Trade;
using Nosum.Infrastructure.Visits;

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

        NetworkApplication app = BuildApplication(WebSocketPort, null, IsDevelopment());

        // Listings past their expiry give their items back to the sellers (docs/adr/ADR-013).
        IMarketService market = InstanceManager.Instance.GetExistingInstance<IMarketService>()
            ?? throw new InvalidOperationException("The market service is not registered.");
        Task marketExpiry = new MarketExpiryService(market, TimeProvider.System).RunAsync(
            MarketExpiryService.DefaultInterval,
            ex => Console.Error.WriteLine("Market expiry failed: " + ex.Message),
            shutdown.Token);

        await using (app.ConfigureAwait(false))
        {
            await app.RunAsync(shutdown.Token).ConfigureAwait(false);
        }

        await guestCleanup.ConfigureAwait(false);
        await marketExpiry.ConfigureAwait(false);

        return 0;
    }

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
    /// Builds the server calendar: the default (docs/adr/ADR-018), with the epoch replaced by <c>NOSUM_CALENDAR_EPOCH</c> (an ISO 8601 UTC
    /// instant, for example <c>2026-11-02T00:00:00Z</c>) when that variable is set. An unreadable value stops the process rather than
    /// silently starting a different calendar.
    /// </summary>
    /// <returns>The calendar every player shares.</returns>
    private static GameCalendar ConfiguredCalendar()
    {
        string? text = Environment.GetEnvironmentVariable("NOSUM_CALENDAR_EPOCH");
        if (string.IsNullOrWhiteSpace(text))
        {
            return GameCalendar.Default;
        }

        if (!DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTimeOffset epoch))
        {
            throw new InvalidOperationException("NOSUM_CALENDAR_EPOCH is not a valid ISO 8601 instant.");
        }

        return new GameCalendar(epoch.ToUnixTimeMilliseconds(), GameCalendar.DefaultDayLengthMs);
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
            .UseSecureConnections()
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
            .MapHandlers(typeof(VisitHandlers))
            .MapHandlers(typeof(ShopHandlers))
            .MapHandlers(typeof(CalendarHandlers));

        // Tester tools exist only in Development: no handler, no service, no opcode in Production.
        if (development)
        {
            builder = builder.MapHandlers(typeof(DevHandlers));
        }

        return builder
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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The player, trade and co-op services live as long as the process; the snapshot loop ends by itself when the last player leaves.")]
    private static void RegisterServices(TimeProvider clock, bool development)
    {
        IConnectionGroupRegistry groups = new InMemoryGroupStore();
        InstanceManager.Instance.Register(groups);

        MapPresence presence = new(groups);
        TmxMapCatalog maps = new(GameMaps.All, Path.Combine(AppContext.BaseDirectory, "maps"));
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()
            ?? throw new InvalidOperationException("HostServices.Register must run before BuildApplication.");
        InstanceManager.Instance.Register<IMapPresence>(presence);
        InstanceManager.Instance.Register<IMapCatalog>(maps);
        SessionRegistry sessions = new(presence);
        InstanceManager.Instance.Register<ISessionRegistry>(sessions);

        // The server calendar every player shares (docs/adr/ADR-018).
        GameCalendar calendar = ConfiguredCalendar();
        InstanceManager.Instance.Register<ICalendarService>(new CalendarService(calendar, clock));

        // Items, quests and farming (docs/adr/ADR-011). One notifier pushes to a player's active connection.
        IUnitOfWorkFactory unitOfWork = InstanceManager.Instance.GetExistingInstance<IUnitOfWorkFactory>()
            ?? throw new InvalidOperationException("HostServices.Register must run before BuildApplication.");
        IRegistrationPromptService prompts = InstanceManager.Instance.GetExistingInstance<IRegistrationPromptService>()
            ?? throw new InvalidOperationException("HostServices.Register must run before BuildApplication.");
        PlayerNotifier notifier = new(sessions);
        InstanceManager.Instance.Register<IPlayerNotifier>(notifier);
        InventoryService inventory = new(unitOfWork, clock, notifier);
        QuestService quests = new(unitOfWork, clock, notifier);
        // The first finished quest invites a guest to register.
        InstanceManager.Instance.Register(new RegistrationPromptOnQuestCompleted(quests, prompts, notifier));
        InstanceManager.Instance.Register<IInventoryService>(inventory);
        InstanceManager.Instance.Register<IQuestService>(quests);

        // Farm and farmhouse are private per player; placed furniture is part of the instance a player walks on (docs/adr/ADR-015).
        PlaceableService placeables = new(presence, maps, inventory, unitOfWork, clock);
        InstanceManager.Instance.Register<IPlaceableService>(placeables);
        // Entry to a private place is decided by the owner's allow-list and co-op setting (docs/adr/ADR-019).
        VisitAccess access = new(unitOfWork);
        PlayerService players = new(presence, maps, states, placeables, access, TimeProvider.System, SnapshotInterval);
        InstanceManager.Instance.Register<IPlayerService>(players);
        InstanceManager.Instance.Register<IFarmService>(new FarmService(presence, maps, inventory, quests, unitOfWork, clock, calendar));

        // Coins, market, looks, trade and co-ops (docs/adr/ADR-013, ADR-014) all move items through the one transfer service.
        TransferService transfers = new(inventory, clock, notifier);
        InstanceManager.Instance.Register<ITransferService>(transfers);
        INicknameDirectory nicknames = InstanceManager.Instance.GetExistingInstance<INicknameDirectory>()
            ?? throw new InvalidOperationException("HostServices.Register must run before BuildApplication.");

        // The market fee is a coin sink (owner decision 2026-10-25); IFeeSink is the one place that would redirect it.
        InstanceManager.Instance.Register<IFeeSink>(new CoinSink());
        InstanceManager.Instance.Register<IMarketService>(new MarketService(transfers, inventory, unitOfWork, presence, maps, new CoinSink(), nicknames, clock));
        InstanceManager.Instance.Register<IShopService>(new ShopService(transfers, presence, maps, calendar, clock));
        if (development)
        {
            InstanceManager.Instance.Register<IDevGrantService>(new DevGrantService(transfers));
        }

        InstanceManager.Instance.Register<ILookService>(new LookService(unitOfWork, inventory, presence, nicknames, clock));
        InstanceManager.Instance.Register<ITradeService>(new TradeService(presence, inventory, transfers, notifier, clock));
        CoopService coops = new(unitOfWork, transfers, presence, sessions, notifier, nicknames, clock);
        InstanceManager.Instance.Register<ICoopService>(coops);
        InstanceManager.Instance.Register<IVisitService>(new VisitService(unitOfWork, presence, players, access, nicknames, coops, clock));
    }
}
