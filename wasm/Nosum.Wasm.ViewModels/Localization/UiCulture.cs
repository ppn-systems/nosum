// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Globalization;
using Nosum.Contracts.Common;

namespace Nosum.Wasm.ViewModels.Localization;

/// <summary>Applies the player's language to the UI (docs/adr/ADR-010 D6).</summary>
public static class UiCulture
{
    /// <summary>The language of the current UI culture, as the wire enum.</summary>
    public static GameLanguage Current => GameLanguages.FromTag(CultureInfo.CurrentUICulture.Name);

    /// <summary>Sets the current and default culture for UI text and formatting.</summary>
    /// <param name="language">The language; <see cref="GameLanguage.Unspecified"/> selects the default.</param>
    public static void Apply(GameLanguage language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(GameLanguages.ToTag(language));
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
