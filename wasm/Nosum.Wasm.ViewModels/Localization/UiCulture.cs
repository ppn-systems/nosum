// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Globalization;

namespace Nosum.Wasm.ViewModels.Localization;

/// <summary>Applies the player's language to the UI (docs/adr/ADR-010 D6).</summary>
public static class UiCulture
{
    /// <summary>Sets the current and default culture for UI text and formatting; an unknown tag changes nothing.</summary>
    /// <param name="languageTag">BCP-47 tag such as <c>vi</c> or <c>en</c>.</param>
    public static void Apply(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return;
        }

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(languageTag);
        }
        catch (CultureNotFoundException)
        {
            return;
        }

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
