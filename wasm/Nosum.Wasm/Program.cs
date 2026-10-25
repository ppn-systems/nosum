// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Nosum.Rendering.Web;

namespace Nosum.Wasm;

/// <summary>
/// Entry point for the Nosum browser client.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class Program
{
    public static async Task Main(string[] args)
    {
        WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");
        builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

        await WebRenderBackend.ImportModuleAsync().ConfigureAwait(false);
        await builder.Build().RunAsync().ConfigureAwait(false);
    }
}
