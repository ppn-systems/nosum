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
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Look;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Market;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Progress;
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
using Nosum.Handlers.GroundItem;
using Nosum.Handlers.Inventory;
using Nosum.Handlers.Look;
using Nosum.Handlers.Market;
using Nosum.Handlers.Node;
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
using Nosum.Infrastructure.GroundItems;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Looks;
using Nosum.Infrastructure.Maps;
using Nosum.Infrastructure.Market;
using Nosum.Infrastructure.Nodes;
using Nosum.Infrastructure.Placeables;
using Nosum.Infrastructure.Players;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Presence;
using Nosum.Infrastructure.Progress;
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

        // Items that lay on a shared map for ten minutes vanish (docs/adr/ADR-021 D6).
        IGroundItems groundItems = InstanceManager.Instance.GetExistingInstance<IGroundItems>()
            ?? throw new InvalidOperationException("The ground item service is not registered.");
        Task groundItemExpiry = new GroundItemExpiryService(groundItems, TimeProvider.System).RunAsync(
            GroundItemExpiryService.DefaultInterval,
            ex => Console.Error.WriteLine("Ground item expiry failed: " + ex.Message),
            shutdown.Token);

        await using (app.ConfigureAwait(false))
        {
            await app.RunAsync(shutdown.Token).ConfigureAwait(false);
        }

        await guestCleanup.ConfigureAwait(false);
        await marketExpiry.ConfigureAwait(false);
        await groundItemExpiry.ConfigureAwait(false);

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
    /// silently starting a different calendar. Without the variable, a development server started before the default epoch counts from the last Monday.
    /// </summary>
    /// <param name="development">Whether the host runs in the Development environment.</param>
    /// <param name="clock">The server clock.</param>
    /// <returns>The calendar every player shares.</returns>
    private static GameCalendar ConfiguredCalendar(bool development, TimeProvider clock)
    {
        string? text = Environment.GetEnvironmentVariable("NOSUM_CALENDAR_EPOCH");
        if (string.IsNullOrWhiteSpace(text))
        {
            // Before the epoch no game day passes; a development server started earlier would never grow a crop, so it counts from the last Monday instead.
            long nowMs = clock.GetUtcNow().ToUnixTimeMilliseconds();
            if (development && nowMs < GameCalendar.DefaultEpochMs)
            {
                DateTimeOffset today = clock.GetUtcNow().UtcDateTime.Date;
                DateTimeOffset monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                return new GameCalendar(new DateTimeOffset(monday.UtcDateTime, TimeSpan.Zero).ToUnixTimeMilliseconds(), GameCalendar.DefaultDayLengthMs);
            }

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
            .MapHandlers(typeof(NodeHandlers))
            .MapHandlers(typeof(VisitHandlers))
            .MapHandlers(typeof(ShopHandlers))
            .MapHandlers(typeof(CalendarHandlers))
            .MapHandlers(typeof(GroundItemHandlers));

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
    private static void RegisterServices(TimeProvider systemClock, bool development)
    {
        // Development: every service reads the time through a clock the dev tools can move forward (docs/adr/ADR-021 D7).
        DevTimeProvider? devClock = development ? new DevTimeProvider(systemClock) : null;
        TimeProvider clock = devClock ?? systemClock;

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
        GameCalendar calendar = ConfiguredCalendar(development, clock);
        InstanceManager.Instance.Register<ICalendarService>(new CalendarService(calendar, clock));

        // One timer on the calendar's day boundaries; services subscribe to the day-ended event for side effects (docs/adr/ADR-021 D7).
        DayClockService dayClock = new(calendar, clock, ex => Console.Error.WriteLine("Day-ended handler failed: " + ex.Message));
        InstanceManager.Instance.Register<IDayClock>(dayClock);

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

        // Farm and farmhouse are private per player; placed furniture and resource nodes are part of the instance a player walks on (docs/adr/ADR-015, ADR-021).
        PlaceableService placeables = new(presence, maps, inventory, unitOfWork, clock);
        InstanceManager.Instance.Register<IPlaceableService>(placeables);
        // Entry to a private place is decided by the owner's allow-list and co-op setting (docs/adr/ADR-019).
        VisitAccess access = new(unitOfWork);
        // Co-op land (docs/adr/ADR-020) is entered by membership; the player service also moves a removed member out.
        CoopMembership coopMembership = new(unitOfWork);
        InstanceManager.Instance.Register<ICoopMembership>(coopMembership);

        // Coins, market, looks, trade, co-ops and ground-item pickups (docs/adr/ADR-013, ADR-014, ADR-021) all move items through the one transfer service.
        TransferService transfers = new(inventory, clock, notifier);
        InstanceManager.Instance.Register<ITransferService>(transfers);

        // Items lying in the world: harvests and broken nodes drop here, walking players pick them up (docs/adr/ADR-021 D5-D6).
        GroundItemService groundItems = new(presence, maps, transfers, unitOfWork, clock);
        InstanceManager.Instance.Register<IGroundItems>(groundItems);
        InstanceManager.Instance.Register<IYieldRandom>(new YieldRandom((ulong)Random.Shared.NextInt64()));

        // The swing cooldown runs on real time (like the snapshot loop), whatever clock crops grow by.
        ToolStateService toolState = new(TimeProvider.System, notifier);
        InstanceManager.Instance.Register<IToolState>(toolState);
        CalendarGameDayClock nodeDayClock = new(calendar, clock);
        InstanceManager.Instance.Register<IGameDayClock>(nodeDayClock);
        // Energy and skills (docs/adr/ADR-023): every tool use is charged through this in the transaction of its effect.
        PlayerProgressService progress = new(unitOfWork, nodeDayClock, inventory, clock, notifier);
        InstanceManager.Instance.Register<IPlayerProgress>(progress);
        ResourceNodeService nodes = new(presence, maps, inventory, new GroundYieldSink(groundItems, unitOfWork, presence, maps), toolState, nodeDayClock, unitOfWork, clock, progress);

        // Trees grow and public places regrow debris once a game day (docs/adr/ADR-021 D4), on the one day clock every daily rule uses.
        // The day that starts after day N ended is node day N (node days count from 0); a new season starts every DaysPerSeason days.
        dayClock.DayEnded += ended =>
        {
            long today = ended.DayNumber;
            bool seasonChanged = today % GameCalendar.DaysPerSeason == 0;
            _ = nodes.AdvanceDayAsync(today, seasonChanged).ContinueWith(
                static t => Console.Error.WriteLine("Node day rollover failed: " + t.Exception?.GetBaseException().Message),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        };
        InstanceManager.Instance.Register<IResourceNodeService>(nodes);
        PlayerService players = new(presence, maps, states, new InstanceWorld(placeables, nodes), access, coopMembership, TimeProvider.System, SnapshotInterval, groundItems);
        InstanceManager.Instance.Register<IPlayerService>(players);
        InstanceManager.Instance.Register<ICoopLandEviction>(players);
        FarmService farm = new(presence, maps, inventory, quests, coopMembership, nodes, toolState, unitOfWork, clock, calendar, groundItems, progress, dayClock);
        InstanceManager.Instance.Register<IFarmService>(farm);

        INicknameDirectory nicknames = InstanceManager.Instance.GetExistingInstance<INicknameDirectory>()
            ?? throw new InvalidOperationException("HostServices.Register must run before BuildApplication.");

        // The market fee is a coin sink (owner decision 2026-10-25); IFeeSink is the one place that would redirect it.
        InstanceManager.Instance.Register<IFeeSink>(new CoinSink());
        InstanceManager.Instance.Register<IMarketService>(new MarketService(transfers, inventory, unitOfWork, presence, maps, new CoinSink(), nicknames, clock));
        InstanceManager.Instance.Register<IShopService>(new ShopService(transfers, presence, maps, calendar, clock));
        if (development)
        {
            InstanceManager.Instance.Register<IDevGrantService>(new DevGrantService(transfers));
            InstanceManager.Instance.Register<IDevClockService>(new DevClockService(devClock!, calendar, dayClock));
        }

        // Armed last: every subscriber is in place before the first boundary.
        dayClock.Start();

        InstanceManager.Instance.Register<ILookService>(new LookService(unitOfWork, inventory, presence, nicknames, clock));
        InstanceManager.Instance.Register<ITradeService>(new TradeService(presence, inventory, transfers, notifier, clock));
        CoopService coops = new(unitOfWork, transfers, presence, sessions, notifier, nicknames, farm, players, clock);
        InstanceManager.Instance.Register<ICoopService>(coops);
        InstanceManager.Instance.Register<IVisitService>(new VisitService(unitOfWork, presence, players, access, nicknames, coops, clock));
    }
}
