// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Nosum.Rendering.Web;
using Nosum.Wasm.Game;
using Nosum.Wasm.Socket;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels.Localization;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm;

/// <summary>
/// Entry point for the Nosum browser client.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class Program
{
    /// <summary>Id of the map the client joins; matches the server catalog.</summary>
    private const string FarmMapId = "farm";

    public static async Task Main(string[] args)
    {
        // Vietnamese is the default language (docs/adr/ADR-010 D1). TODO: apply the account's
        // language here once accounts exist (ADR-010 D6).
        CultureInfo vietnamese = new("vi");
        CultureInfo.DefaultThreadCurrentCulture = vietnamese;
        CultureInfo.DefaultThreadCurrentUICulture = vietnamese;
        CultureInfo.CurrentCulture = vietnamese;
        CultureInfo.CurrentUICulture = vietnamese;

        WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");
        builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

        // One server connection for the app's lifetime (docs/adr/ADR-003 D5).
        builder.Services.AddSingleton(_ => GameSocketOptions.ForPageHost(new Uri(builder.HostEnvironment.BaseAddress)));
        builder.Services.AddSingleton<GameSocket>();
        builder.Services.AddSingleton<IGameSocket>(sp => sp.GetRequiredService<GameSocket>());
        builder.Services.AddSingleton(sp => new PlayerChannel(sp.GetRequiredService<IGameSocket>(), FarmMapId));
        builder.Services.AddSingleton<IStringLocalizer<Language>, ResxStringLocalizer<Language>>();
        builder.Services.AddSingleton(sp =>
        {
            ConnectionStatusViewModel status = new(sp.GetRequiredService<IGameSocket>(), sp.GetRequiredService<IStringLocalizer<Language>>());
            status.Watch(sp.GetRequiredService<PlayerChannel>());
            return status;
        });

        await WebRenderBackend.ImportModuleAsync().ConfigureAwait(false);
        WebAssemblyHost host = builder.Build();

        GameLoop.UseNetwork(host.Services.GetRequiredService<PlayerChannel>());
        _ = host.Services.GetRequiredService<ConnectionStatusViewModel>();
        host.Services.GetRequiredService<IGameSocket>().Start();

        await host.RunAsync().ConfigureAwait(false);
    }
}
