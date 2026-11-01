// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;
using Nosum.Wasm.Device;
using Nosum.Wasm.Security;
using Nosum.Wasm.Settings;
using Nosum.Wasm.ViewModels.Settings;
using Nosum.Wasm.Socket;
using Nosum.Wasm.Socket.Blacksmith;
using Nosum.Wasm.Socket.Building;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Chat;
using Nosum.Wasm.Socket.Chest;
using Nosum.Wasm.Socket.Bestiary;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Coop;
using Nosum.Wasm.Socket.Crafting;
using Nosum.Wasm.Socket.Emote;
using Nosum.Wasm.Socket.Dev;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Fishing;
using Nosum.Wasm.Socket.Friends;
using Nosum.Wasm.Socket.Furnace;
using Nosum.Wasm.Socket.Goals;
using Nosum.Wasm.Socket.GroundItem;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Journal;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.Socket.Mail;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Mine;
using Nosum.Wasm.Socket.Node;
using Nosum.Wasm.Socket.Pet;
using Nosum.Wasm.Socket.Placeable;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.Socket.Quest;
using Nosum.Wasm.Socket.Requests;
using Nosum.Wasm.Socket.Shop;
using Nosum.Wasm.Socket.Trade;
using Nosum.Wasm.Socket.Visit;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.Account;
using Nosum.Wasm.ViewModels.Account;
using Nosum.Wasm.ViewModels.Backpack;
using Nosum.Wasm.ViewModels.Blacksmith;
using Nosum.Wasm.ViewModels.Building;
using Nosum.Wasm.ViewModels.Calendar;
using Nosum.Wasm.ViewModels.Chat;
using Nosum.Wasm.ViewModels.Chest;
using Nosum.Wasm.ViewModels.Coop;
using Nosum.Wasm.ViewModels.Crafting;
using Nosum.Wasm.ViewModels.Device;
using Nosum.Wasm.ViewModels.Emotes;
using Nosum.Wasm.ViewModels.Feedback;
using Nosum.Wasm.ViewModels.Fishing;
using Nosum.Wasm.ViewModels.Friends;
using Nosum.Wasm.ViewModels.Furnace;
using Nosum.Wasm.ViewModels.Goals;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Looks;
using Nosum.Wasm.ViewModels.Journal;
using Nosum.Wasm.ViewModels.Maps;
using Nosum.Wasm.ViewModels.Mail;
using Nosum.Wasm.ViewModels.Market;
using Nosum.Wasm.ViewModels.Mine;
using Nosum.Wasm.ViewModels.Minimap;
using Nosum.Wasm.ViewModels.Npc;
using Nosum.Wasm.ViewModels.Pets;
using Nosum.Wasm.ViewModels.Placement;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Profile;
using Nosum.Wasm.ViewModels.Progress;
using Nosum.Wasm.ViewModels.Quest;
using Nosum.Wasm.ViewModels.Requests;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Shop;
using Nosum.Wasm.ViewModels.Trade;
using Nosum.Wasm.ViewModels.Visit;
using Nosum.Wasm.ViewModels.Startup;
using Nosum.Wasm.ViewModels.Wallet;

namespace Nosum.Wasm.Game;

/// <summary>
/// Registers all client channels and view models in DI and wires the authenticated session lifecycle to <see cref="GameLoop"/>.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class ClientWiring
{
    /// <summary>Registers all game socket channels and UI view models.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="baseAddress">Application base URI.</param>
    /// <param name="configuration">Host configuration.</param>
    public static void AddGameServices(this IServiceCollection services, Uri baseAddress, IConfiguration configuration)
    {
        // One server connection for the app's lifetime (docs/adr/ADR-003 D5).
        // The deploy pins the server's public key in wwwroot/appsettings.json; without it a development build trusts the server on first use (docs/adr/ADR-032 D1).
        services.AddSingleton(sp => GameSocketOptions.ForPageHost(baseAddress)
            .ApplyBootConfiguration(key => configuration[key], () => new BrowserServerKeyStore(sp.GetRequiredService<IJSRuntime>())));
        services.AddSingleton<GameSocket>();
        services.AddSingleton<IGameSocket>(sp => sp.GetRequiredService<GameSocket>());
        services.AddSingleton(sp => new PlayerChannel(sp.GetRequiredService<IGameSocket>(), GameMaps.DefaultId));
        services.AddSingleton<InventoryChannel>();
        services.AddSingleton<FarmChannel>();
        services.AddSingleton<PlaceableChannel>();
        services.AddSingleton<CraftingChannel>();
        services.AddSingleton<ChestChannel>();
        services.AddSingleton(sp => new FurnaceChannel(sp.GetRequiredService<IGameSocket>()));
        services.AddSingleton(sp => new BuildingChannel(sp.GetRequiredService<IGameSocket>()));
        services.AddSingleton(sp => new NodeChannel(sp.GetRequiredService<IGameSocket>()));
        services.AddSingleton<GroundItemChannel>();
        services.AddSingleton(sp => new ProgressChannel(sp.GetRequiredService<IGameSocket>()));
        services.AddSingleton(sp => new CombatChannel(sp.GetRequiredService<IGameSocket>()));
        services.AddSingleton<QuestChannel>();
        services.AddSingleton<WalletChannel>();
        services.AddSingleton<CalendarChannel>();
        services.AddSingleton(sp => new EmoteChannel(sp.GetRequiredService<IGameSocket>()));
        services.AddSingleton<EmoteBubbleStore>();
        services.AddSingleton(sp => new PetChannel(sp.GetRequiredService<IGameSocket>()));
        services.AddSingleton<MarketChannel>();
        services.AddSingleton<TradeChannel>();
        services.AddSingleton<CoopChannel>();
        services.AddSingleton<VisitChannel>();
        services.AddSingleton<ChatMessenger>();
        services.AddSingleton<FriendsChannel>();
        services.AddSingleton<MailChannel>();
        services.AddSingleton<ShopChannel>();
        services.AddSingleton<FishingChannel>();
        services.AddSingleton<BlacksmithChannel>();
        services.AddSingleton<BackpackChannel>();
        services.AddSingleton<MineChannel>();
        services.AddSingleton<GoalChannel>();
        services.AddSingleton<RequestChannel>();
        services.AddSingleton<DevChannel>();
        services.AddSingleton<NearbyPlayers>();
        services.AddSingleton(sp =>
        {
            PlayerChannel player = sp.GetRequiredService<PlayerChannel>();
            return new LookChannel(sp.GetRequiredService<IGameSocket>(), () => player.LocalPlayerId);
        });
        services.AddSingleton<IItemIconSource, ItemIcons>();
        services.AddSingleton<InventoryViewModel>();
        services.AddSingleton<QuestTrackerViewModel>();
        services.AddSingleton<PlacementViewModel>();
        services.AddSingleton(sp => new FurnitureCatalogViewModel(
            PlaceableCatalog.All,
            sp.GetRequiredService<InventoryChannel>(),
            sp.GetRequiredService<PlacementViewModel>(),
            sp.GetRequiredService<ItemDescriber>(),
            sp.GetRequiredService<IStringLocalizer<Catalogue>>(),
            sp.GetRequiredService<IStringLocalizer<Language>>()));
        services.AddSingleton<GameMessagesViewModel>();
        services.AddSingleton<ThirstWarning>();
        services.AddSingleton<LevelUpBannerViewModel>();
        services.AddSingleton<SeasonBannerViewModel>();
        services.AddSingleton<ItemDescriber>();
        services.AddSingleton<ToolbarViewModel>();
        services.AddSingleton<WorldMapViewModel>();
        services.AddSingleton<MinimapViewModel>();
        services.AddSingleton<JournalChannel>();
        services.AddSingleton<JournalViewModel>();
        services.AddSingleton<NicknameFormatter>();
        services.AddSingleton<ProfileViewModel>();
        services.AddSingleton<IHaptics, BrowserHaptics>();
        services.AddSingleton<ShopViewModel>();
        services.AddSingleton<NpcDialogViewModel>();
        services.AddSingleton<BlacksmithViewModel>();
        services.AddSingleton<BackpackViewModel>();
        services.AddSingleton<ElevatorViewModel>();
        services.AddSingleton(sp => new FishingViewModel(
            sp.GetRequiredService<FishingChannel>(),
            sp.GetRequiredService<ItemDescriber>(),
            sp.GetRequiredService<IStringLocalizer<Language>>()));
        services.AddSingleton<MapLoadingViewModel>();
        services.AddSingleton<GameStartViewModel>();
        services.AddSingleton<RequestsViewModel>();
        services.AddSingleton<GoalsViewModel>();
        services.AddSingleton<BestiaryChannel>();
        services.AddSingleton(sp => new ProgressViewModel(
            sp.GetRequiredService<ProgressChannel>(),
            sp.GetRequiredService<IStringLocalizer<Language>>(),
            sp.GetRequiredService<CombatChannel>(),
            sp.GetRequiredService<IStringLocalizer<Catalogue>>(),
            sp.GetRequiredService<BestiaryChannel>()));
        services.AddSingleton(sp => new EmoteWheelViewModel(
            sp.GetRequiredService<EmoteChannel>(),
            sp.GetRequiredService<EmoteBubbleStore>(),
            () => sp.GetRequiredService<PlayerChannel>().LocalPlayerId,
            sp.GetRequiredService<IStringLocalizer<Language>>()));
        services.AddSingleton(sp => new PetViewModel(sp.GetRequiredService<PetChannel>(), sp.GetRequiredService<IStringLocalizer<Language>>()));
        services.AddSingleton<WalletViewModel>();
        services.AddSingleton<CalendarViewModel>();
        services.AddSingleton<MarketViewModel>();
        services.AddSingleton<WardrobeViewModel>();
        services.AddSingleton<TradeViewModel>();
        services.AddSingleton<CoopViewModel>();
        services.AddSingleton<CraftingViewModel>();
        services.AddSingleton<ChestViewModel>();
        services.AddSingleton<FurnaceViewModel>();
        services.AddSingleton<CarpenterViewModel>();
        services.AddSingleton<AnimalCareViewModel>();
        services.AddSingleton<VisitorsViewModel>();
        services.AddSingleton<FriendsViewModel>();
        services.AddSingleton(sp => new ChatViewModel(
            sp.GetRequiredService<ChatMessenger>(),
            sp.GetRequiredService<NicknameFormatter>(),
            sp.GetRequiredService<IStringLocalizer<Language>>(),
            () => sp.GetRequiredService<ProfileViewModel>().IsGuest));
        services.AddSingleton(sp => new MailViewModel(
            sp.GetRequiredService<MailChannel>(),
            sp.GetRequiredService<ItemDescriber>(),
            sp.GetRequiredService<NicknameFormatter>(),
            sp.GetRequiredService<IStringLocalizer<Language>>(),
            sp.GetRequiredService<CalendarChannel>().ServerNowMs));
        services.AddSingleton<IAccountClient, GameSocketAccountClient>();
        services.AddSingleton<AccountSessionKeeper>();
        services.AddSingleton<IRememberedSessionStore>(sp => new BrowserRememberedSessionStore(sp.GetRequiredService<IJSRuntime>()));
        services.AddSingleton(sp => new RememberedSessionViewModel(
            sp.GetRequiredService<IAccountClient>(),
            sp.GetRequiredService<IRememberedSessionStore>(),
            sp.GetRequiredService<IStringLocalizer<Language>>(),
            TimeProvider.System));
        services.AddTransient<AccountFormViewModel>();
        services.AddSingleton(sp =>
        {
            ConnectionStatusViewModel status = new(sp.GetRequiredService<IGameSocket>(), sp.GetRequiredService<IStringLocalizer<Language>>());
            status.Watch(sp.GetRequiredService<PlayerChannel>());
            return status;
        });
    }

    /// <summary>Wires channels, session lifecycle hooks, and view model subscriptions into <see cref="GameLoop"/>.</summary>
    /// <param name="services">Built host service provider.</param>
    /// <param name="devicePolicy">Active device policy.</param>
    public static void Connect(IServiceProvider services, DevicePolicy devicePolicy)
    {
        PlayerChannel channel = services.GetRequiredService<PlayerChannel>();
        InventoryChannel inventory = services.GetRequiredService<InventoryChannel>();
        FarmChannel farm = services.GetRequiredService<FarmChannel>();
        PlaceableChannel placeable = services.GetRequiredService<PlaceableChannel>();
        NodeChannel nodes = services.GetRequiredService<NodeChannel>();
        GameLoop.UseNodes(nodes);
        GroundItemChannel groundItems = services.GetRequiredService<GroundItemChannel>();
        QuestChannel quests = services.GetRequiredService<QuestChannel>();
        MineChannel mine = services.GetRequiredService<MineChannel>();
        GameLoop.UseNetwork(channel);
        GameLoop.UseGroundItems(groundItems);
        MarketChannel market = services.GetRequiredService<MarketChannel>();
        WalletChannel wallet = services.GetRequiredService<WalletChannel>();
        CoopChannel coop = services.GetRequiredService<CoopChannel>();
        LookChannel looks = services.GetRequiredService<LookChannel>();
        GameLoop.UseFarming(farm, inventory, market);
        GameLoop.UseCoop(coop);
        GameLoop.UseProgress(services.GetRequiredService<ProgressChannel>());
        CombatChannel combat = services.GetRequiredService<CombatChannel>();
        GameLoop.UseCombat(combat, services.GetRequiredService<IStringLocalizer<Language>>());
        GameLoop.UseCalendar(services.GetRequiredService<CalendarChannel>());
        GameLoop.UseWorldMap(services.GetRequiredService<WorldMapViewModel>());
        GameLoop.UseMinimap(services.GetRequiredService<MinimapViewModel>());
        GameLoop.UseMapLoading(services.GetRequiredService<MapLoadingViewModel>());
        GameLoop.UseFishing(services.GetRequiredService<FishingViewModel>(), services.GetRequiredService<SettingsViewModel>());
        GameLoop.UseLooks(looks, services.GetRequiredService<NicknameFormatter>());
#if NOSUM_DEV_TOOLS
        DevTools.Use(services.GetRequiredService<DevChannel>(), services.GetRequiredService<FishingViewModel>(), combat);
#endif
        FurnaceChannel furnaces = services.GetRequiredService<FurnaceChannel>();
        GameLoop.UseContainers(services.GetRequiredService<ChestChannel>(), furnaces);
        BuildingChannel buildings = services.GetRequiredService<BuildingChannel>();
        GameLoop.UseBuildings(buildings);
        GameLoop.UsePlacement(placeable, services.GetRequiredService<PlacementViewModel>());
        PetChannel pets = services.GetRequiredService<PetChannel>();
        GameLoop.UsePets(pets);
        GameLoop.UseEmotes(services.GetRequiredService<EmoteBubbleStore>());

        // Every time the connection is signed in (first sign-in and each resume) the server-side state is read again;
        // the farm, the placed furniture and the resource nodes are per place, so they load whenever a map was joined.
        AccountSessionKeeper keeper = services.GetRequiredService<AccountSessionKeeper>();
        SettingsWiring.Connect(services);
        AudioWiring.Connect(new GameAudio(), services);
        keeper.Authenticated += channel.Join;
        keeper.Authenticated += inventory.Load;
        keeper.Authenticated += quests.Load;
        keeper.Authenticated += wallet.Load;
        keeper.Authenticated += services.GetRequiredService<CalendarChannel>().Load;
        keeper.Authenticated += coop.Load;
        keeper.Authenticated += services.GetRequiredService<MailChannel>().Load;
        keeper.Authenticated += combat.LoadVitals;
        keeper.Authenticated += pets.Load;

        channel.Joined += _ =>
        {
            farm.Load();
            placeable.Load();
            furnaces.Load();
            nodes.Load();
            groundItems.Load();
            combat.LoadVitals();
            combat.LoadMonsters();
            // The merchants' lines follow the deepest floor (docs/adr/ADR-080 D2), so the mine's progress is read on join too.
            mine.Load();
        };
        channel.Joined += _ => looks.Load();
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
        _ = services.GetRequiredService<InventoryViewModel>();
        _ = services.GetRequiredService<QuestTrackerViewModel>();
        _ = services.GetRequiredService<LevelUpBannerViewModel>();
        GameMessagesViewModel messages = services.GetRequiredService<GameMessagesViewModel>();
        _ = services.GetRequiredService<ThirstWarning>();
        IStringLocalizer<Language> localizer = services.GetRequiredService<IStringLocalizer<Language>>();
        combat.KnockedOut += () => messages.Notify(localizer["Combat_KnockedOut"].Value);
        combat.AttackCompleted += attack =>
        {
            if (attack.KnockedOut)
            {
                messages.Notify(localizer["Combat_KnockedOut"].Value);
            }
        };
        combat.SkillCompleted += skill =>
        {
            // A refused cast says why, so a press the server does not accept is never silent (docs/adr/ADR-056 D5).
            string? refusal = SkillRefusalText.KeyFor(skill.Status);
            if (refusal is not null)
            {
                messages.Notify(localizer[refusal].Value);
            }
        };
        GameLoop.SkillPressRefused += key => messages.Notify(localizer[key].Value);
        GameLoop.ActionKeyHarvested += () => messages.Notify(localizer["Farm_ActionKeyHarvested"].Value);
        _ = services.GetRequiredService<NearbyPlayers>();
        ProgressViewModel progressVm = services.GetRequiredService<ProgressViewModel>();
        GameLoop.UseRunState(state =>
        {
            progressVm.SetRun(state);
            if (state == RunState.OutOfEnergy)
            {
                messages.Notify(localizer["Run_OutOfEnergy"].Value);
            }
        });
        _ = services.GetRequiredService<WalletViewModel>();
        _ = services.GetRequiredService<CalendarViewModel>();
        _ = services.GetRequiredService<SeasonBannerViewModel>();
        _ = services.GetRequiredService<GoalChannel>();
        _ = services.GetRequiredService<TradeViewModel>();
        _ = services.GetRequiredService<CoopViewModel>();
        _ = services.GetRequiredService<ChatViewModel>();
        _ = services.GetRequiredService<FriendsViewModel>();
        _ = services.GetRequiredService<MailViewModel>();
        _ = services.GetRequiredService<ChestViewModel>();
        _ = services.GetRequiredService<FurnaceViewModel>();
        _ = services.GetRequiredService<CarpenterViewModel>();
        _ = services.GetRequiredService<AnimalCareViewModel>();
        _ = services.GetRequiredService<EmoteWheelViewModel>();
        _ = services.GetRequiredService<PetViewModel>();
        ConnectionStatusViewModel connection = services.GetRequiredService<ConnectionStatusViewModel>();
        GameLoop.SetConnectionBlocked(connection.BlocksInput);
        connection.Changed += () => GameLoop.SetConnectionBlocked(connection.BlocksInput);

        DeviceGuardViewModel device = services.GetRequiredService<DeviceGuardViewModel>();
        BrowserDevice.WatchWidth(devicePolicy.MinimumWidth, _ => device.UpdateWidth(BrowserDevice.ViewportWidth()));

        // A phone or touch screen collapses the HUD menu column; the game menu then lists the column panels as tiles (docs/design/ui-standard.md section 6).
        ToolbarViewModel toolbar = services.GetRequiredService<ToolbarViewModel>();
        toolbar.ColumnCollapsed = BrowserDevice.MenuColumnCollapsed();
        BrowserDevice.WatchMenuColumn(collapsed => toolbar.ColumnCollapsed = collapsed);

        // Closing or reloading the tab asks first while the player is in the game (page-guard.js).
        keeper.Authenticated += () => BrowserDevice.SetUnloadGuard(true);
        keeper.SessionEnded += _ => BrowserDevice.SetUnloadGuard(false);
        keeper.LoggedOut += () => BrowserDevice.SetUnloadGuard(false);

        if (device.Verdict != DeviceVerdict.TouchBlocked)
        {
            services.GetRequiredService<IGameSocket>().Start();
        }
    }
}
