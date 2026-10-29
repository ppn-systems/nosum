// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Nosum.Contracts.Common;
using Nosum.Gameplay.Maps;
using Nosum.Wasm.Game;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.ViewModels.Settings;

namespace Nosum.Wasm.Settings;

/// <summary>Start-up wiring of the client settings and the first-run hints (docs/adr/ADR-033), kept out of <c>Program.cs</c>.</summary>
[SupportedOSPlatform("browser")]
internal static class SettingsWiring
{
    /// <summary>Loads the settings script and reads the language the browser used last, so the sign-in screen starts in it.</summary>
    /// <returns>The language.</returns>
    public static async Task<GameLanguage> LoadLanguageAsync()
    {
        await BrowserSettings.ImportAsync().ConfigureAwait(false);
        return ClientSettings.Parse(BrowserSettings.Read()).Language;
    }

    /// <summary>Registers the settings and hint view models.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddClientSettings(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsStore, LocalSettingsStore>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<OnboardingViewModel>();
        services.AddSingleton<SettingsApplier>();
        return services;
    }

    /// <summary>Applies the stored settings and connects the things the first-run hints watch.</summary>
    /// <param name="services">The built service provider.</param>
    public static void Connect(IServiceProvider services)
    {
        SettingsViewModel settings = services.GetRequiredService<SettingsViewModel>();
        SettingsApplier applier = services.GetRequiredService<SettingsApplier>();
        settings.Applied += current => applier.Apply(current, settings.ReducesMotion);
        settings.SetSystemReducesMotion(BrowserSettings.PrefersReducedMotion());
        settings.Apply();

        OnboardingViewModel hints = services.GetRequiredService<OnboardingViewModel>();
        GameLoop.FirstMoved += () => hints.Signal(TutorialStep.Move);
        GameLoop.FirstActed += () => hints.Signal(TutorialStep.Use);

        // Coins going up after the first balance is known: something was sold (at the bin, at the market).
        long coins = -1;
        WalletChannel wallet = services.GetRequiredService<WalletChannel>();
        wallet.Changed += () =>
        {
            long now = wallet.Coins;
            if (coins >= 0 && now > coins)
            {
                hints.Signal(TutorialStep.Sell);
            }

            coins = now;
        };

        ToolbarViewModel toolbar = services.GetRequiredService<ToolbarViewModel>();
        toolbar.Changed += () =>
        {
            if (toolbar.Active == PanelKind.Shop)
            {
                hints.Signal(TutorialStep.Shop);
            }
        };

        services.GetRequiredService<PlayerChannel>().Joined += mapId =>
        {
            if (string.Equals(mapId, GameMaps.Town, StringComparison.Ordinal))
            {
                hints.Signal(TutorialStep.Town);
            }
        };
    }
}
