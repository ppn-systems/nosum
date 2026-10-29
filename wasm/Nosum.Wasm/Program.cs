// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Nosum.Rendering.Web;
using Nosum.Wasm.Device;
using Nosum.Wasm.Game;
using Nosum.Wasm.Settings;
using Nosum.Wasm.ViewModels.Device;
using Nosum.Wasm.ViewModels.Localization;
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
        // The language this browser used last (Vietnamese on a first visit) until sign-in applies the account's language (docs/adr/ADR-010 D1, D6, docs/adr/ADR-033).
        UiCulture.SetDefault(await SettingsWiring.LoadLanguageAsync().ConfigureAwait(false));

        WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");
        Uri baseAddress = new(builder.HostEnvironment.BaseAddress);
        builder.Services.AddScoped(_ => new HttpClient { BaseAddress = baseAddress });
        builder.Services.AddLocalization();

        // The device gate (wasm/README.md): a phone or tablet gets the notice instead of the game and no connection is opened for it.
        DevicePolicy devicePolicy = BrowserDevice.ReadPolicy(builder.Configuration);
        DeviceSignals deviceSignals = BrowserDevice.ReadSignals();
        builder.Services.AddSingleton(sp => new DeviceGuardViewModel(sp.GetRequiredService<IStringLocalizer<Language>>(), deviceSignals, devicePolicy));
        builder.Services.AddClientSettings();
        builder.Services.AddGameServices(baseAddress, builder.Configuration);

        await WebRenderBackend.ImportModuleAsync().ConfigureAwait(false);
        WebAssemblyHost host = builder.Build();
        ClientWiring.Connect(host.Services, devicePolicy);

        await host.RunAsync().ConfigureAwait(false);
    }
}
