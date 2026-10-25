// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using Nosum.Contracts.Common;

namespace Nosum.Wasm.ViewModels.Localization;

/// <summary>Applies the player's language to the UI (docs/adr/ADR-010 D6).</summary>
public static class UiCulture
{
    /// <summary>Raised after <see cref="Apply"/> changed the language, so view models that keep localized text can build it again. May run on any thread.</summary>
    public static event Action? Changed;

    /// <summary>The language of the current UI culture, as the wire enum.</summary>
    public static GameLanguage Current => GameLanguages.FromTag(CultureInfo.CurrentUICulture.Name);

    /// <summary>
    /// Sets only the default culture, which every execution context without a culture of its own follows. The start-up uses this: setting the
    /// current culture there would pin it in the start-up context (it is stored per execution context), and a later <see cref="Apply"/> would not reach
    /// the event handlers and socket callbacks that descend from it, so half of the UI would stay in the first language.
    /// </summary>
    /// <param name="language">The language; <see cref="GameLanguage.Unspecified"/> selects the default.</param>
    public static void SetDefault(GameLanguage language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(GameLanguages.ToTag(language));
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    /// <summary>Sets the current and default culture for UI text and formatting.</summary>
    /// <param name="language">The language; <see cref="GameLanguage.Unspecified"/> selects the default.</param>
    public static void Apply(GameLanguage language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(GameLanguages.ToTag(language));
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Changed?.Invoke();
    }
}
