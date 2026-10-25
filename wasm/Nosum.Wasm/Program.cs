// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace Nosum.Wasm;

/// <summary>
/// Entry point for the Nosum browser client.
/// </summary>
internal static class Program
{
    public static async Task Main(string[] args)
    {
        WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        await builder.Build().RunAsync().ConfigureAwait(false);
    }
}
