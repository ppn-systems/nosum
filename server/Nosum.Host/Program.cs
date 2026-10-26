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
using Nalix.Network.Options;
using Nalix.Runtime.Middleware.Standard;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Admin;
using Nosum.Abstractions.Observability;
using Nosum.Abstractions.Blacksmith;
using Nosum.Abstractions.Buildings;
using Nosum.Abstractions.Calendar;
using Nosum.Abstractions.Chat;
using Nosum.Abstractions.Community;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Crafting;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Friends;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Look;
using Nosum.Abstractions.Mail;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Market;
using Nosum.Abstractions.Mines;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Progress;
using Nosum.Abstractions.Shops;
using Nosum.Abstractions.Quests;
using Nosum.Abstractions.Requests;
using Nosum.Abstractions.Trade;
using Nosum.Abstractions.Visits;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Maps;
using Nosum.Handlers.Account;
using Nosum.Handlers.Admin;
using Nosum.Handlers.Blacksmith;
using Nosum.Handlers.Building;
using Nosum.Handlers.Calendar;
using Nosum.Handlers.Chat;
using Nosum.Handlers.Coop;
using Nosum.Handlers.Dev;
using Nosum.Handlers.Farm;
using Nosum.Handlers.Friends;
using Nosum.Handlers.Goals;
using Nosum.Handlers.GroundItem;
using Nosum.Handlers.Inventory;
using Nosum.Handlers.Look;
using Nosum.Handlers.Mail;
using Nosum.Handlers.Market;
using Nosum.Handlers.Mine;
using Nosum.Handlers.Node;
using Nosum.Handlers.Placeable;
using Nosum.Handlers.Security;
using Nosum.Handlers.Player;
using Nosum.Handlers.Quest;
using Nosum.Handlers.Requests;
using Nosum.Handlers.Shop;
using Nosum.Handlers.Trade;
using Nosum.Handlers.Visit;
using Nosum.Infrastructure.Accounts;
using Nosum.Host.Ops;
using Nosum.Infrastructure.Blacksmith;
using Nosum.Infrastructure.Caching;
using Nosum.Infrastructure.Buildings;
using Nosum.Infrastructure.Calendar;
using Nosum.Infrastructure.Chat;
using Nosum.Infrastructure.Coops;
using Nosum.Infrastructure.Crafting;
using Nosum.Infrastructure.Dev;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Farm;
using Nosum.Infrastructure.Friends;
using Nosum.Infrastructure.GroundItems;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Looks;
using Nosum.Infrastructure.Community;
using Nosum.Infrastructure.Mail;
using Nosum.Infrastructure.Maps;
using Nosum.Infrastructure.Market;
using Nosum.Infrastructure.Mines;
using Nosum.Infrastructure.Nodes;
using Nosum.Infrastructure.Placeables;
using Nosum.Infrastructure.Players;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Presence;
using Nosum.Infrastructure.Progress;
using Nosum.Infrastructure.Quests;
using Nosum.Infrastructure.Requests;
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

    // Everything after the ops endpoint is up: migrate, register, serve until the shutdown token fires.
    private static async Task<int> RunServerAsync(DbContextOptions<NosumDbContext> database, CancellationTokenSource shutdown)
    {
        await NosumDatabase.MigrateAsync(database, shutdown.Token).ConfigureAwait(false);
        HostServices.Register(database);

        // Idle guests (30 days without coming back) are deleted once an hour.
        Task guestCleanup = new GuestCleanupService(database, TimeProvider.System).RunAsync(
            GuestCleanupService.DefaultInterval,
            ex => Console.Error.WriteLine("Guest cleanup failed: " + ex.Message),
            shutdown.Token);

        NetworkApplication app = BuildApplication(WebSocketPort, null, IsDevelopment());
        await OpsServices.LoadMutesAsync(shutdown.Token).ConfigureAwait(false);

        // Goals of seasons that ended while the server was down expire now, and rewards a crash left unpaid are paid (docs/adr/ADR-030 D3).
        ICommunityGoalService goals = InstanceManager.Instance.GetExistingInstance<ICommunityGoalService>()
            ?? throw new InvalidOperationException("The community goal service is not registered.");
        try
        {
            _ = await goals.CloseEndedSeasonsAsync(shutdown.Token).ConfigureAwait(false);
            _ = await goals.SettlePendingAsync(shutdown.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Community goal start-up sweep failed: " + ex.Message).ConfigureAwait(false);
        }

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

        // Animals inside a building with a player in it pick a new walk every few seconds (docs/adr/ADR-028 D4).
        AnimalWanderService wander = InstanceManager.Instance.GetExistingInstance<AnimalWanderService>()
            ?? throw new InvalidOperationException("The animal wander service is not registered.");
        Task animalWander = wander.RunAsync(
            AnimalWanderService.DefaultInterval,
            ex => Console.Error.WriteLine("Animal wander failed: " + ex.Message),
            shutdown.Token);

        // Mail past its 30 days is removed with its attachment (docs/adr/ADR-029 D5).
        IMailService mail = InstanceManager.Instance.GetExistingInstance<IMailService>()
            ?? throw new InvalidOperationException("The mail service is not registered.");
        Task mailExpiry = new MailExpiryService(mail, TimeProvider.System).RunAsync(
            MailExpiryService.DefaultInterval,
            ex => Console.Error.WriteLine("Mail expiry failed: " + ex.Message),
            shutdown.Token);

        if (LoadTestMode())
        {
            OpsLog.Warning("host.load_test_mode", "NOSUM_LOAD_TEST is on: the per-address rate limits are OFF. Never run players on this server.");
        }

        OpsLog.Information("host.started", "The server is accepting connections.", new LogField("ws_port", (int)WebSocketPort));
        Task keyAnnouncement = ServerKey.AnnounceAsync(Console.WriteLine, shutdown.Token);
        await using (app.ConfigureAwait(false))
        {
            await app.RunAsync(shutdown.Token).ConfigureAwait(false);
        }

        // Whatever waits for its write goes to the database before the process ends (docs/adr/ADR-035 D7).
        await (InstanceManager.Instance.GetExistingInstance<WriteBehind>() ?? throw new InvalidOperationException("The write-behind queue is not registered.")).FlushAllAsync(CancellationToken.None).ConfigureAwait(false);

        await keyAnnouncement.ConfigureAwait(false);
        await animalWander.ConfigureAwait(false);

        await guestCleanup.ConfigureAwait(false);
        await marketExpiry.ConfigureAwait(false);
        await groundItemExpiry.ConfigureAwait(false);
        await mailExpiry.ConfigureAwait(false);

        return 0;
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
            .MapHandlers(typeof(VisitHandlers))
            .MapHandlers(typeof(ShopHandlers))
            .MapHandlers(typeof(BlacksmithHandlers))
            .MapHandlers(typeof(MineHandlers))
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

        // Community unlocks (docs/adr/ADR-030) open the gated tiles of the maps (the Beach bridge, docs/adr/ADR-037 D4), so they exist before the catalogue.
        CommunityUnlocks communityUnlocks = new(InstanceManager.Instance.GetExistingInstance<IUnitOfWorkFactory>()
            ?? throw new InvalidOperationException("HostServices.Register must run before BuildApplication."));
        TmxMapCatalog maps = new(GameMaps.All, Path.Combine(AppContext.BaseDirectory, "maps"), communityUnlocks.IsUnlocked);
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()
            ?? throw new InvalidOperationException("HostServices.Register must run before BuildApplication.");
        InstanceManager.Instance.Register<IMapPresence>(presence);
        InstanceManager.Instance.Register<IMapCatalog>(maps);

        // Chunks (docs/adr/ADR-022): the index from chunk to interested players, and the one door a world event goes through.
        ChunkPublisher publisher = new(presence, presence);
        InstanceManager.Instance.Register<IChunkMap>(presence);
        InstanceManager.Instance.Register<IChunkPublisher>(publisher);
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
        // The few writes whose loss changes no money, item or progress wait up to five seconds and are written when the player leaves and at shutdown (docs/adr/ADR-035 D7).
        WriteBehind writeBehind = new(unitOfWork, TimeProvider.System);
        writeBehind.Failed += ex => Console.Error.WriteLine("A delayed write failed and was dropped: " + ex.Message);
        InstanceManager.Instance.Register(writeBehind);
        presence.PlayerLeft += player => new ValueTask(writeBehind.FlushAsync(player.AccountId));
        sessions.SessionEnded += id => new ValueTask(writeBehind.FlushAsync(id));
        InventoryService inventory = new(unitOfWork, clock, notifier, writeBehind);
        // Coins, market, looks, trade, co-ops and ground-item pickups (docs/adr/ADR-013, ADR-014, ADR-021) all move items through the one transfer service; a quest's reward is one too.
        TransferService transfers = new(inventory, clock, notifier);
        InstanceManager.Instance.Register<ITransferService>(transfers);
        QuestService quests = new(unitOfWork, clock, notifier, transfers);
        // The first finished quest invites a guest to register.
        InstanceManager.Instance.Register(new RegistrationPromptOnQuestCompleted(quests, prompts, notifier));
        InstanceManager.Instance.Register<IInventoryService>(inventory);
        // A player who signed out lets go of their in-memory inventory (docs/adr/ADR-035 D3).
        PlayerStateCache playerStates = InstanceManager.Instance.GetExistingInstance<PlayerStateCache>()
            ?? throw new InvalidOperationException("HostServices.Register must run before BuildApplication.");
        // The saved place stays in memory: the leave saves it through the same cache (docs/adr/ADR-035 D4), so dropping it here would race that save.
        sessions.SessionEnded += id =>
        {
            inventory.Forget(id);
            quests.Forget(id);
            return ValueTask.CompletedTask;
        };
        InstanceManager.Instance.Register<IQuestService>(quests);

        INicknameDirectory nicknames = InstanceManager.Instance.GetExistingInstance<INicknameDirectory>()
            ?? throw new InvalidOperationException("HostServices.Register must run before BuildApplication.");

        // Farm and farmhouse are private per player; placed objects and resource nodes are part of the instance a player walks on (docs/adr/ADR-015, ADR-021, ADR-024).
        // What stands where is published through one shared index so the farm layer and the growth rule can read it without needing the placeables built first.
        PlacedTiles placedTiles = new();
        InstanceManager.Instance.Register<IPlacedLookup>(placedTiles);
        // Every world feature publishes through the chunk publisher and sends its chunk's state when it joins a player's interest set (docs/adr/ADR-022).
        // Co-op land (docs/adr/ADR-020) is entered by membership; the player service also moves a removed member out.
        CoopDirectory coopDirectory = new(unitOfWork);
        sessions.SessionEnded += id =>
        {
            coopDirectory.Forget(id);
            return ValueTask.CompletedTask;
        };
        CoopMembership coopMembership = new(unitOfWork, coopDirectory);
        InstanceManager.Instance.Register<ICoopMembership>(coopMembership);
        // Farm buildings (docs/adr/ADR-028): their doors are known to the movement path, and the inside of a building is entered by whoever may enter its land.
        BuildingDirectory buildingDirectory = new(unitOfWork);
        InstanceManager.Instance.Register<IBuildingDirectory>(buildingDirectory);
        // Entry to a private place is decided by the owner's allow-list and co-op setting (docs/adr/ADR-019); the inside of a building follows its land (docs/adr/ADR-028 D2).
        // Friends and the owners' visit settings are held in memory too (docs/adr/ADR-035 D5): the access check reads them on every entry, the friend, visit and block services change them.
        FriendFacts friendFacts = new(unitOfWork);
        VisitFacts visitFacts = new(unitOfWork);
        InstanceManager.Instance.Register(friendFacts);
        InstanceManager.Instance.Register(visitFacts);
        InstanceManager.Instance.Register(coopDirectory);
        sessions.SessionEnded += id =>
        {
            friendFacts.Changed(id);
            return ValueTask.CompletedTask;
        };
        BlockList blocks = new(unitOfWork, nicknames, clock, friendFacts);
        InstanceManager.Instance.Register<IBlockList>(blocks);
        BuildingAccessPolicy access = new(new VisitAccess(unitOfWork, nicknames, coopDirectory, visitFacts, friendFacts, blocks), buildingDirectory, coopMembership, clock);

        // Admin intents, the mute list and the live gauges (docs/adr/ADR-031 D6-D7).
        OpsServices.Register(unitOfWork, sessions, transfers, presence, clock);

        // Items lying in the world: harvests and broken nodes drop here, walking players pick them up (docs/adr/ADR-021 D5-D6).
        GroundItemService groundItems = new(presence, presence, publisher, maps, transfers, unitOfWork, clock);
        InstanceManager.Instance.Register<IGroundItems>(groundItems);
        InstanceManager.Instance.Register<IYieldRandom>(new YieldRandom((ulong)Random.Shared.NextInt64()));

        // The swing cooldown runs on real time (like the snapshot loop), whatever clock crops grow by.
        ToolStateService toolState = new(TimeProvider.System, notifier);
        InstanceManager.Instance.Register<IToolState>(toolState);
        CalendarGameDayClock nodeDayClock = new(calendar, clock);
        InstanceManager.Instance.Register<IGameDayClock>(nodeDayClock);
        // Energy and skills (docs/adr/ADR-023): every tool use is charged through this in the transaction of its effect.
        PlayerProgressService progress = new(unitOfWork, nodeDayClock, inventory, clock, notifier, playerStates);
        InstanceManager.Instance.Register<IPlayerProgress>(progress);
        ResourceNodeService nodes = new(presence, presence, publisher, maps, inventory, new GroundYieldSink(groundItems, unitOfWork, presence, maps), toolState, nodeDayClock, unitOfWork, clock, progress, placedTiles);

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
        FarmService farm = new(presence, presence, publisher, maps, inventory, quests, coopMembership, nodes, toolState, unitOfWork, clock, calendar, groundItems, progress, dayClock, placedTiles);

        // Placing objects, chests and furnaces (docs/adr/ADR-024): a chest and a furnace refuse to be picked up while they hold something, so they attach to the placeables.
        PlaceableService placeables = new(presence, presence, publisher, maps, inventory, unitOfWork, clock, farm, nodes, coopMembership, placedTiles);
        InstanceManager.Instance.Register<IPlaceableService>(placeables);
        ChestService chests = new(presence, maps, placeables, inventory, coopMembership, clock);
        InstanceManager.Instance.Register<IChestService>(chests);
        placeables.Attach(chests);
        FurnaceService furnaces = new(presence, maps, placeables, placedTiles, placeables, inventory, unitOfWork, coopMembership, calendar, clock);
        InstanceManager.Instance.Register<IFurnaceService>(furnaces);
        placeables.Attach(furnaces);
        // Coops and barns (docs/adr/ADR-028): placed from a blueprint the carpenter sells, finished two game days later; the animals walk inside while players watch.
        AnimalWanderService animalWander = new(presence, publisher, maps, buildingDirectory, unitOfWork, clock, (ulong)Random.Shared.NextInt64());
        InstanceManager.Instance.Register(animalWander);
        InstanceManager.Instance.Register<IAnimalWander>(animalWander);
        BuildingService buildings = new(presence, publisher, maps, inventory, transfers, buildingDirectory, animalWander, unitOfWork, coopMembership, calendar, clock);
        InstanceManager.Instance.Register<IBuildingService>(buildings);
        placeables.Attach(buildings);
        // Recipe unlocks follow the player's skill levels (docs/adr/ADR-023 D3, ADR-024 D1).
        InstanceManager.Instance.Register<ICraftingService>(new CraftingService(presence, inventory, new ProgressSkillLevels(progress), quests));
        LookService looks = new(unitOfWork, inventory, presence, nicknames, presence, publisher, clock);
        InstanceManager.Instance.Register<ILookService>(looks);
        ChunkStreamer streamer = new(presence, publisher, looks, [placeables, farm, nodes, groundItems]);
        PlayerService players = new(presence, presence, streamer, maps, states, new InstanceWorld(placeables, nodes, buildingDirectory), access, coopMembership, TimeProvider.System, SnapshotInterval, groundItems, buildingDirectory, communityUnlocks);
        InstanceManager.Instance.Register<IPlayerService>(players);
        InstanceManager.Instance.Register<ICoopLandEviction>(players);

        // The mine (docs/adr/ADR-026): floors are per-player instances built in memory, dropped when the player leaves; a tool use that ends at a ladder takes the player down,
        // so the farm service handlers see is the decorated one (the placeables and the co-ops keep the plain one they were built with).
        MineService mine = new(presence, maps, players, unitOfWork, nodes, groundItems);
        InstanceManager.Instance.Register<IMineService>(mine);
        InstanceManager.Instance.Register<IFarmService>(new MineLadderFarmService(farm, mine));

        // Block lists and the mailbox (docs/adr/ADR-029): the trade, co-op and visit services drop requests between blocked players, the market and the
        // co-ops send mail, and community goals send rewards through IMailService.
        MailService mail = new(unitOfWork, transfers, notifier, clock, writeBehind);
        InstanceManager.Instance.Register<IMailService>(mail);

        // The market fee is a coin sink (owner decision 2026-10-25); IFeeSink is the one place that would redirect it.
        InstanceManager.Instance.Register<IFeeSink>(new CoinSink());
        InstanceManager.Instance.Register<IMarketService>(new MarketService(transfers, inventory, unitOfWork, presence, maps, new CoinSink(), nicknames, clock, calendar, mail, quests));
        InstanceManager.Instance.Register<IShopService>(new ShopService(transfers, presence, maps, calendar, clock, quests));
        InstanceManager.Instance.Register<IBlacksmithService>(new BlacksmithService(transfers, inventory, unitOfWork, presence, maps, toolState, calendar, clock));

        // Community goals (docs/adr/ADR-030): deliveries at the Town board, unlock flags, and expiry when a season ends on the one day clock.
        InstanceManager.Instance.Register<ICommunityUnlocks>(communityUnlocks);
        CommunityGoalService communityGoals = new(transfers, unitOfWork, presence, maps, calendar, clock, communityUnlocks, mail);
        InstanceManager.Instance.Register<ICommunityGoalService>(communityGoals);
        dayClock.DayEnded += ended =>
        {
            _ = ended;
            _ = communityGoals.CloseEndedSeasonsAsync().ContinueWith(
                static t => Console.Error.WriteLine("Community goal season sweep failed: " + t.Exception?.GetBaseException().Message),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        };

        // The Town requests board (docs/adr/ADR-036): the next day's requests are written when a day ends; a read after a restart writes them lazily.
        RequestService requests = new(transfers, unitOfWork, presence, maps, calendar, clock);
        InstanceManager.Instance.Register<IRequestService>(requests);
        dayClock.DayEnded += ended =>
        {
            _ = requests.EnsureDayAsync(ended.DayNumber + 1).ContinueWith(
                static t => Console.Error.WriteLine("Request day roll failed: " + t.Exception?.GetBaseException().Message),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        };
        if (development)
        {
            InstanceManager.Instance.Register<IDevGrantService>(new DevGrantService(transfers));
            InstanceManager.Instance.Register<IDevClockService>(new DevClockService(devClock!, calendar, dayClock));
        }

        InstanceManager.Instance.Register<ITradeService>(new TradeService(presence, inventory, transfers, notifier, clock, blocks));
        CoopService coops = new(unitOfWork, transfers, inventory, presence, sessions, notifier, nicknames, farm, players, clock, mail, blocks, coopDirectory);
        InstanceManager.Instance.Register<ICoopService>(coops);

        // Friends (a visiting allow-list source) and chat (docs/adr/ADR-029).
        FriendService friends = new(unitOfWork, presence, sessions, notifier, nicknames, blocks, clock, friendFacts, visitFacts);
        InstanceManager.Instance.Register<IFriendService>(friends);
        InstanceManager.Instance.Register<IVisitService>(new VisitService(unitOfWork, presence, players, access, nicknames, coops, clock, friends, buildingDirectory, visitFacts));
        InstanceManager.Instance.Register<IChatService>(new ChatService(presence, sessions, notifier, nicknames, blocks, unitOfWork, clock, presence, InstanceManager.Instance.GetExistingInstance<IMuteList>(), coopDirectory));

        // Armed last: every subscriber is in place before the first boundary.
        dayClock.Start();
    }
}
