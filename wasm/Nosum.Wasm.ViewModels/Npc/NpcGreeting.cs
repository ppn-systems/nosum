// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Npcs;
using Nosum.Gameplay.Weather;

namespace Nosum.Wasm.ViewModels.Npc;

/// <summary>
/// Chooses which greeting a merchant says (docs/adr/ADR-043 D7, issue #130): a line for the weather of the day first, then one for the season, then the
/// plain <c>Greeting</c>. A variant is optional: a merchant with no line for the day keeps the plain greeting. Rain and storm share the rain line.
/// </summary>
public static class NpcGreeting
{
    /// <summary>Picks the key of the greeting to show.</summary>
    /// <param name="npc">The merchant.</param>
    /// <param name="weather">Today's weather, or <see langword="null"/> while the calendar is not known.</param>
    /// <param name="season">The season, or <see langword="null"/> while the calendar is not known.</param>
    /// <param name="exists">Whether a <c>Language.resx</c> key has text.</param>
    /// <returns>The first key that has text: weather, season, then <c>Npc_{Id}_Greeting</c>.</returns>
    public static string KeyFor(NpcDefinition npc, WeatherKind? weather, Season? season, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(exists);

        if (weather is { } kind)
        {
            string? part = kind switch
            {
                WeatherKind.Rain or WeatherKind.Storm => "Rain",
                WeatherKind.Snow => "Snow",
                _ => null,
            };

            if (part is not null && exists(npc.TextKey("Greeting_" + part)))
            {
                return npc.TextKey("Greeting_" + part);
            }
        }

        if (season is { } current && exists(npc.TextKey("Greeting_" + current)))
        {
            return npc.TextKey("Greeting_" + current);
        }

        return npc.TextKey("Greeting");
    }
}
