// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Maps;
using Nosum.Wasm.Device;
using Nosum.Rendering.Web;
using Nosum.Wasm.Game;
using Nosum.Wasm.Security;
using Nosum.Wasm.Settings;
using Nosum.Wasm.Socket;
using Nosum.Wasm.Socket.Building;
using Nosum.Wasm.Socket.Chat;
using Nosum.Wasm.Socket.Chest;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Coop;
using Nosum.Wasm.Socket.Crafting;
using Nosum.Wasm.Socket.Friends;
using Nosum.Wasm.Socket.Furnace;
using Nosum.Wasm.Socket.Mail;
using Nosum.Wasm.Socket.Visit;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.Socket.Goals;
using Nosum.Wasm.Socket.Requests;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Node;
using Nosum.Wasm.Socket.Dev;
using Nosum.Wasm.Socket.GroundItem;
using Nosum.Wasm.Socket.Placeable;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.Socket.Quest;
using Nosum.Wasm.Socket.Shop;
using Nosum.Wasm.Socket.Trade;
using Nosum.Contracts.Common;
using Nosum.Wasm.ViewModels.Account;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.ViewModels.Building;
using Nosum.Wasm.ViewModels.Chat;
using Nosum.Wasm.ViewModels.Chest;
using Nosum.Wasm.ViewModels.Coop;
using Nosum.Wasm.ViewModels.Crafting;
using Nosum.Wasm.ViewModels.Friends;
using Nosum.Wasm.ViewModels.Furnace;
using Nosum.Wasm.ViewModels.Mail;
using Nosum.Wasm.ViewModels.Visit;
using Nosum.Wasm.ViewModels.Feedback;
using Nosum.Wasm.ViewModels.Help;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Localization;
using Nosum.Wasm.ViewModels.Looks;
using Nosum.Wasm.ViewModels.Goals;
using Nosum.Wasm.ViewModels.Requests;
using Nosum.Wasm.ViewModels.Market;
using Nosum.Wasm.ViewModels.Placement;
using Nosum.Wasm.ViewModels.Profile;
using Nosum.Wasm.ViewModels.Progress;
using Nosum.Wasm.ViewModels.Shop;
using Nosum.Wasm.ViewModels.Blacksmith;
using Nosum.Wasm.ViewModels.Mine;
using Nosum.Wasm.ViewModels.Npc;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Quest;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Trade;
using Nosum.Wasm.ViewModels.Wallet;
using Nosum.Wasm.ViewModels.Calendar;
using Nosum.Wasm.ViewModels.Device;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Blacksmith;
using Nosum.Wasm.Socket.Mine;

namespace Nosum.Wasm;

/// <summary>
/// Entry point for the Nosum browser client.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class Program
{
    public static async Task Main(string[] args)
    {
        // The language this browser used last (Vietnamese on a first visit) until sign-in applies the account's language (docs/adr/ADR-010 D1, D6, docs/adr/ADR-033).
        UiCulture.SetDefault(await SettingsWiring.LoadLanguageAsync().ConfigureAwait(false));

        WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");
        builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

        builder.Services.AddLocalization();

        // The device gate (wasm/README.md): a phone or tablet gets the notice instead of the game and no connection is opened for it.
        DevicePolicy devicePolicy = BrowserDevice.ReadPolicy(builder.Configuration);
        DeviceSignals deviceSignals = BrowserDevice.ReadSignals();
        builder.Services.AddSingleton(sp => new DeviceGuardViewModel(sp.GetRequiredService<IStringLocalizer<Language>>(), deviceSignals, devicePolicy));
        builder.Services.AddClientSettings();

        // One server connection for the app's lifetime (docs/adr/ADR-003 D5).
        // The deploy pins the server's public key in wwwroot/appsettings.json; without it a development build trusts the server on first use (docs/adr/ADR-032 D1).
        builder.Services.AddSingleton(sp => GameSocketOptions.ForPageHost(new Uri(builder.HostEnvironment.BaseAddress))
            .ApplyBootConfiguration(key => builder.Configuration[key], () => new BrowserServerKeyStore(sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>())));
        builder.Services.AddSingleton<GameSocket>();
        builder.Services.AddSingleton<IGameSocket>(sp => sp.GetRequiredService<GameSocket>());
        builder.Services.AddSingleton(sp => new PlayerChannel(sp.GetRequiredService<IGameSocket>(), GameMaps.DefaultId));
        builder.Services.AddSingleton<InventoryChannel>();
        builder.Services.AddSingleton<FarmChannel>();
        builder.Services.AddSingleton<PlaceableChannel>();
        builder.Services.AddSingleton<CraftingChannel>();
        builder.Services.AddSingleton<ChestChannel>();
        builder.Services.AddSingleton(sp => new FurnaceChannel(sp.GetRequiredService<IGameSocket>()));
        builder.Services.AddSingleton(sp => new BuildingChannel(sp.GetRequiredService<IGameSocket>()));
        builder.Services.AddSingleton(sp => new NodeChannel(sp.GetRequiredService<IGameSocket>()));
        builder.Services.AddSingleton<GroundItemChannel>();
        builder.Services.AddSingleton(sp => new ProgressChannel(sp.GetRequiredService<IGameSocket>()));
        builder.Services.AddSingleton(sp => new CombatChannel(sp.GetRequiredService<IGameSocket>()));
        builder.Services.AddSingleton<QuestChannel>();
        builder.Services.AddSingleton<WalletChannel>();
        builder.Services.AddSingleton<CalendarChannel>();
        builder.Services.AddSingleton<MarketChannel>();
        builder.Services.AddSingleton<TradeChannel>();
        builder.Services.AddSingleton<CoopChannel>();
        builder.Services.AddSingleton<VisitChannel>();
        builder.Services.AddSingleton<ChatMessenger>();
        builder.Services.AddSingleton<FriendsChannel>();
        builder.Services.AddSingleton<MailChannel>();
        builder.Services.AddSingleton<ShopChannel>();
        builder.Services.AddSingleton<BlacksmithChannel>();
        builder.Services.AddSingleton<MineChannel>();
        builder.Services.AddSingleton<GoalChannel>();
        builder.Services.AddSingleton<RequestChannel>();
        builder.Services.AddSingleton<DevChannel>();
        builder.Services.AddSingleton<NearbyPlayers>();
        builder.Services.AddSingleton(sp =>
        {
            PlayerChannel player = sp.GetRequiredService<PlayerChannel>();
            return new LookChannel(sp.GetRequiredService<IGameSocket>(), () => player.LocalPlayerId);
        });
        builder.Services.AddSingleton<IItemIconSource, ItemIcons>();
        builder.Services.AddSingleton<InventoryViewModel>();
        builder.Services.AddSingleton<QuestTrackerViewModel>();
        builder.Services.AddSingleton<PlacementViewModel>();
        builder.Services.AddSingleton<GameMessagesViewModel>();
        builder.Services.AddSingleton<ItemDescriber>();
        builder.Services.AddSingleton<ToolbarViewModel>();
        builder.Services.AddSingleton<NicknameFormatter>();
        builder.Services.AddSingleton<ProfileViewModel>();
        builder.Services.AddSingleton<ShopViewModel>();
        builder.Services.AddSingleton<NpcDialogViewModel>();
        builder.Services.AddSingleton<BlacksmithViewModel>();
        builder.Services.AddSingleton<ElevatorViewModel>();
        builder.Services.AddSingleton<RequestsViewModel>();
        builder.Services.AddSingleton<GoalsViewModel>();
        builder.Services.AddSingleton<HelpViewModel>();
        builder.Services.AddSingleton(sp => new ProgressViewModel(
            sp.GetRequiredService<ProgressChannel>(),
            sp.GetRequiredService<IStringLocalizer<Language>>(),
            sp.GetRequiredService<CombatChannel>()));
        builder.Services.AddSingleton<WalletViewModel>();
        builder.Services.AddSingleton<CalendarViewModel>();
        builder.Services.AddSingleton<MarketViewModel>();
        builder.Services.AddSingleton<WardrobeViewModel>();
        builder.Services.AddSingleton<TradeViewModel>();
        builder.Services.AddSingleton<CoopViewModel>();
        builder.Services.AddSingleton<CraftingViewModel>();
        builder.Services.AddSingleton<ChestViewModel>();
        builder.Services.AddSingleton<FurnaceViewModel>();
        builder.Services.AddSingleton<CarpenterViewModel>();
        builder.Services.AddSingleton<AnimalCareViewModel>();
        builder.Services.AddSingleton<VisitorsViewModel>();
        builder.Services.AddSingleton<FriendsViewModel>();
        builder.Services.AddSingleton(sp => new ChatViewModel(
            sp.GetRequiredService<ChatMessenger>(),
            sp.GetRequiredService<NicknameFormatter>(),
            sp.GetRequiredService<IStringLocalizer<Language>>(),
            () => sp.GetRequiredService<ProfileViewModel>().IsGuest));
        builder.Services.AddSingleton(sp => new MailViewModel(
            sp.GetRequiredService<MailChannel>(),
            sp.GetRequiredService<ItemDescriber>(),
            sp.GetRequiredService<NicknameFormatter>(),
            sp.GetRequiredService<IStringLocalizer<Language>>(),
            sp.GetRequiredService<CalendarChannel>().ServerNowMs));
        builder.Services.AddSingleton<IAccountClient, GameSocketAccountClient>();
        builder.Services.AddSingleton<AccountSessionKeeper>();
        builder.Services.AddTransient<AccountFormViewModel>();
        // Account recovery (docs/adr/ADR-032 D4).
        builder.Services.AddSingleton<IRecoveryClient, GameSocketRecoveryClient>();
        builder.Services.AddSingleton<PasswordRecoveryViewModel>();
        builder.Services.AddSingleton(sp =>
        {
            ConnectionStatusViewModel status = new(sp.GetRequiredService<IGameSocket>(), sp.GetRequiredService<IStringLocalizer<Language>>());
            status.Watch(sp.GetRequiredService<PlayerChannel>());
            return status;
        });

        await WebRenderBackend.ImportModuleAsync().ConfigureAwait(false);
        WebAssemblyHost host = builder.Build();

        PlayerChannel channel = host.Services.GetRequiredService<PlayerChannel>();
        InventoryChannel inventory = host.Services.GetRequiredService<InventoryChannel>();
        FarmChannel farm = host.Services.GetRequiredService<FarmChannel>();
        PlaceableChannel placeable = host.Services.GetRequiredService<PlaceableChannel>();
        NodeChannel nodes = host.Services.GetRequiredService<NodeChannel>();
        GameLoop.UseNodes(nodes);
        GroundItemChannel groundItems = host.Services.GetRequiredService<GroundItemChannel>();
        QuestChannel quests = host.Services.GetRequiredService<QuestChannel>();
        GameLoop.UseNetwork(channel);
        GameLoop.UseGroundItems(groundItems);
        MarketChannel market = host.Services.GetRequiredService<MarketChannel>();
        WalletChannel wallet = host.Services.GetRequiredService<WalletChannel>();
        CoopChannel coop = host.Services.GetRequiredService<CoopChannel>();
        LookChannel looks = host.Services.GetRequiredService<LookChannel>();
        GameLoop.UseFarming(farm, inventory, market);
        GameLoop.UseCoop(coop);
        GameLoop.UseProgress(host.Services.GetRequiredService<ProgressChannel>());
        CombatChannel combat = host.Services.GetRequiredService<CombatChannel>();
        GameLoop.UseCombat(combat);
        GameLoop.UseCalendar(host.Services.GetRequiredService<CalendarChannel>());
        GameLoop.UseLooks(looks, host.Services.GetRequiredService<NicknameFormatter>());
#if NOSUM_DEV_TOOLS
        DevTools.Use(host.Services.GetRequiredService<DevChannel>());
#endif
        FurnaceChannel furnaces = host.Services.GetRequiredService<FurnaceChannel>();
        GameLoop.UseContainers(host.Services.GetRequiredService<ChestChannel>(), furnaces);
        BuildingChannel buildings = host.Services.GetRequiredService<BuildingChannel>();
        GameLoop.UseBuildings(buildings);
        GameLoop.UsePlacement(placeable, host.Services.GetRequiredService<PlacementViewModel>());

        // Every time the connection is signed in (first sign-in and each resume) the server-side state is read again;
        // the farm, the placed furniture and the resource nodes are per place, so they load whenever a map was joined.
        AccountSessionKeeper keeper = host.Services.GetRequiredService<AccountSessionKeeper>();
        SettingsWiring.Connect(host.Services);
        AudioWiring.Connect(new GameAudio(), host.Services);
        keeper.Authenticated += channel.Join;
        keeper.Authenticated += inventory.Load;
        keeper.Authenticated += quests.Load;
        keeper.Authenticated += wallet.Load;
        keeper.Authenticated += host.Services.GetRequiredService<CalendarChannel>().Load;
        keeper.Authenticated += coop.Load;
        // The unread-mail badge on the gear is live from sign-in: read the mailbox once, then the server pushes every change.
        keeper.Authenticated += host.Services.GetRequiredService<MailChannel>().Load;
        keeper.Authenticated += combat.LoadVitals;
        channel.Joined += _ =>
        {
            farm.Load();
            placeable.Load();
            furnaces.Load();
            nodes.Load();
            groundItems.Load();
            combat.LoadVitals();
            combat.LoadMonsters();
        };
        channel.Joined += _ => looks.Load();
        // Farm buildings (docs/adr/ADR-028): a farm lists its buildings (those still under construction are drawn dull); inside one, the animals are read.
        channel.Joined += mapId =>
        {
            bool known = GameMaps.TryGet(mapId, out MapSpec? spec);
            if (known && spec!.IsInterior)
            {
                buildings.ClearHere();
                buildings.LoadRoom();
                return;
            }

            buildings.ClearRoom();
            if (known && spec!.Farmable)
            {
                buildings.LoadHere();
            }
            else
            {
                buildings.ClearHere();
            }
        };
        // The view models subscribe to their channels when they are built: build them before the first push can arrive.
        _ = host.Services.GetRequiredService<InventoryViewModel>();
        _ = host.Services.GetRequiredService<QuestTrackerViewModel>();
        _ = host.Services.GetRequiredService<GameMessagesViewModel>();
        _ = host.Services.GetRequiredService<NearbyPlayers>();
        _ = host.Services.GetRequiredService<ProgressViewModel>();
        _ = host.Services.GetRequiredService<WalletViewModel>();
        _ = host.Services.GetRequiredService<CalendarViewModel>();
        _ = host.Services.GetRequiredService<GoalChannel>();
        _ = host.Services.GetRequiredService<TradeViewModel>();
        _ = host.Services.GetRequiredService<CoopViewModel>();
        _ = host.Services.GetRequiredService<ChatViewModel>();
        _ = host.Services.GetRequiredService<FriendsViewModel>();
        _ = host.Services.GetRequiredService<MailViewModel>();
        _ = host.Services.GetRequiredService<ChestViewModel>();
        _ = host.Services.GetRequiredService<FurnaceViewModel>();
        _ = host.Services.GetRequiredService<CarpenterViewModel>();
        _ = host.Services.GetRequiredService<AnimalCareViewModel>();
        _ = host.Services.GetRequiredService<ConnectionStatusViewModel>();

        DeviceGuardViewModel device = host.Services.GetRequiredService<DeviceGuardViewModel>();
        BrowserDevice.WatchWidth(devicePolicy.MinimumWidth, _ => device.UpdateWidth(BrowserDevice.ViewportWidth()));

        // Closing or reloading the tab asks first while the player is in the game (page-guard.js).
        keeper.Authenticated += () => BrowserDevice.SetUnloadGuard(true);
        keeper.SessionEnded += _ => BrowserDevice.SetUnloadGuard(false);
        keeper.LoggedOut += () => BrowserDevice.SetUnloadGuard(false);

        if (device.Verdict != DeviceVerdict.TouchBlocked)
        {
            host.Services.GetRequiredService<IGameSocket>().Start();
        }

        await host.RunAsync().ConfigureAwait(false);
    }
}
