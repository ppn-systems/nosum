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
using Nosum.Rendering.Web;
using Nosum.Wasm.Game;
using Nosum.Wasm.Socket;
using Nosum.Wasm.Socket.Coop;
using Nosum.Wasm.Socket.Visit;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.Socket.Market;
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
using Nosum.Wasm.ViewModels.Coop;
using Nosum.Wasm.ViewModels.Visit;
using Nosum.Wasm.ViewModels.Feedback;
using Nosum.Wasm.ViewModels.Help;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Localization;
using Nosum.Wasm.ViewModels.Looks;
using Nosum.Wasm.ViewModels.Market;
using Nosum.Wasm.ViewModels.Placement;
using Nosum.Wasm.ViewModels.Profile;
using Nosum.Wasm.ViewModels.Shop;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Quest;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Trade;
using Nosum.Wasm.ViewModels.Wallet;
using Nosum.Wasm.ViewModels.Calendar;
using Nosum.Wasm.Socket.Calendar;

namespace Nosum.Wasm;

/// <summary>
/// Entry point for the Nosum browser client.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class Program
{
    public static async Task Main(string[] args)
    {
        // Vietnamese until sign-in applies the account's language (docs/adr/ADR-010 D1, D6).
        UiCulture.SetDefault(GameLanguage.Vietnamese);

        WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");
        builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

        builder.Services.AddLocalization();

        // One server connection for the app's lifetime (docs/adr/ADR-003 D5).
        builder.Services.AddSingleton(_ => GameSocketOptions.ForPageHost(new Uri(builder.HostEnvironment.BaseAddress)));
        builder.Services.AddSingleton<GameSocket>();
        builder.Services.AddSingleton<IGameSocket>(sp => sp.GetRequiredService<GameSocket>());
        builder.Services.AddSingleton(sp => new PlayerChannel(sp.GetRequiredService<IGameSocket>(), GameMaps.DefaultId));
        builder.Services.AddSingleton<InventoryChannel>();
        builder.Services.AddSingleton<FarmChannel>();
        builder.Services.AddSingleton<PlaceableChannel>();
        builder.Services.AddSingleton<GroundItemChannel>();
        builder.Services.AddSingleton<QuestChannel>();
        builder.Services.AddSingleton<WalletChannel>();
        builder.Services.AddSingleton<CalendarChannel>();
        builder.Services.AddSingleton<MarketChannel>();
        builder.Services.AddSingleton<TradeChannel>();
        builder.Services.AddSingleton<CoopChannel>();
        builder.Services.AddSingleton<VisitChannel>();
        builder.Services.AddSingleton<ShopChannel>();
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
        builder.Services.AddSingleton<HelpViewModel>();
        builder.Services.AddSingleton<WalletViewModel>();
        builder.Services.AddSingleton<CalendarViewModel>();
        builder.Services.AddSingleton<MarketViewModel>();
        builder.Services.AddSingleton<WardrobeViewModel>();
        builder.Services.AddSingleton<TradeViewModel>();
        builder.Services.AddSingleton<CoopViewModel>();
        builder.Services.AddSingleton<VisitorsViewModel>();
        builder.Services.AddSingleton<IAccountClient, GameSocketAccountClient>();
        builder.Services.AddSingleton<AccountSessionKeeper>();
        builder.Services.AddTransient<AccountFormViewModel>();
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
        GroundItemChannel groundItems = host.Services.GetRequiredService<GroundItemChannel>();
        QuestChannel quests = host.Services.GetRequiredService<QuestChannel>();
        GameLoop.UseNetwork(channel);
        GameLoop.UseGroundItems(groundItems);
        MarketChannel market = host.Services.GetRequiredService<MarketChannel>();
        WalletChannel wallet = host.Services.GetRequiredService<WalletChannel>();
        CoopChannel coop = host.Services.GetRequiredService<CoopChannel>();
        LookChannel looks = host.Services.GetRequiredService<LookChannel>();
        GameLoop.UseFarming(farm, inventory, market);
        GameLoop.UseCalendar(host.Services.GetRequiredService<CalendarChannel>());
        GameLoop.UseLooks(looks, host.Services.GetRequiredService<NicknameFormatter>());
#if NOSUM_DEV_TOOLS
        DevTools.Use(host.Services.GetRequiredService<DevChannel>());
#endif
        GameLoop.UsePlacement(placeable, host.Services.GetRequiredService<PlacementViewModel>());

        // Every time the connection is signed in (first sign-in and each resume) the server-side state is read again;
        // the farm and the placed furniture are per place, so they load whenever a map was joined.
        AccountSessionKeeper keeper = host.Services.GetRequiredService<AccountSessionKeeper>();
        keeper.Authenticated += channel.Join;
        keeper.Authenticated += inventory.Load;
        keeper.Authenticated += quests.Load;
        keeper.Authenticated += wallet.Load;
        keeper.Authenticated += host.Services.GetRequiredService<CalendarChannel>().Load;
        keeper.Authenticated += coop.Load;
        channel.Joined += _ =>
        {
            farm.Load();
            placeable.Load();
            groundItems.Load();
        };
        channel.Joined += _ => looks.Load();
        // The view models subscribe to their channels when they are built: build them before the first push can arrive.
        _ = host.Services.GetRequiredService<InventoryViewModel>();
        _ = host.Services.GetRequiredService<QuestTrackerViewModel>();
        _ = host.Services.GetRequiredService<GameMessagesViewModel>();
        _ = host.Services.GetRequiredService<NearbyPlayers>();
        _ = host.Services.GetRequiredService<WalletViewModel>();
        _ = host.Services.GetRequiredService<CalendarViewModel>();
        _ = host.Services.GetRequiredService<TradeViewModel>();
        _ = host.Services.GetRequiredService<CoopViewModel>();
        _ = host.Services.GetRequiredService<ConnectionStatusViewModel>();
        host.Services.GetRequiredService<IGameSocket>().Start();

        await host.RunAsync().ConfigureAwait(false);
    }
}
