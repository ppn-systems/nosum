// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nosum.Wasm.ViewModels.Account;

namespace Nosum.Wasm.Security;

/// <summary>Registers the Google and Microsoft sign-in from the <c>ExternalSignIn</c> section of <c>appsettings.json</c> (docs/adr/ADR-069).</summary>
internal static class ExternalSignInWiring
{
    private const string DefaultTenant = "common";

    /// <summary>Adds the options and the browser sign-in. A missing or empty value turns that provider's button off.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The boot configuration.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddExternalSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection("ExternalSignIn");
        string tenant = section["MicrosoftTenant"]?.Trim() ?? string.Empty;
        ExternalSignInOptions options = new(
            section["GoogleClientId"]?.Trim() ?? string.Empty,
            section["MicrosoftClientId"]?.Trim() ?? string.Empty,
            tenant.Length == 0 ? DefaultTenant : tenant);

        services.AddSingleton(options);
        services.AddSingleton<IExternalSignIn, BrowserExternalSignIn>();
        return services;
    }
}
