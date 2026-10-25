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
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.Socket.Quest;
using Nosum.Contracts.Common;
using Nosum.Wasm.ViewModels.Account;
using Nosum.Wasm.ViewModels.Feedback;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Localization;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Quest;
using Nosum.Wasm.ViewModels.Resources;

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
        UiCulture.Apply(GameLanguage.Vietnamese);

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
        builder.Services.AddSingleton<QuestChannel>();
        builder.Services.AddSingleton<IItemIconSource, ItemIcons>();
        builder.Services.AddSingleton<InventoryViewModel>();
        builder.Services.AddSingleton<QuestTrackerViewModel>();
        builder.Services.AddSingleton<GameMessagesViewModel>();
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
        QuestChannel quests = host.Services.GetRequiredService<QuestChannel>();
        GameLoop.UseNetwork(channel);
        GameLoop.UseFarming(farm, inventory);

        // Every time the connection is signed in (first sign-in and each resume) the server-side state is read again;
        // the farm is per map, so it loads whenever a map was joined.
        AccountSessionKeeper keeper = host.Services.GetRequiredService<AccountSessionKeeper>();
        keeper.Authenticated += channel.Join;
        keeper.Authenticated += inventory.Load;
        keeper.Authenticated += quests.Load;
        channel.Joined += _ => farm.Load();
        // The view models subscribe to their channels when they are built: build them before the first push can arrive.
        _ = host.Services.GetRequiredService<InventoryViewModel>();
        _ = host.Services.GetRequiredService<QuestTrackerViewModel>();
        _ = host.Services.GetRequiredService<GameMessagesViewModel>();
        _ = host.Services.GetRequiredService<ConnectionStatusViewModel>();
        host.Services.GetRequiredService<IGameSocket>().Start();

        await host.RunAsync().ConfigureAwait(false);
    }
}
