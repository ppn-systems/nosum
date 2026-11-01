// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Weather;

/// <summary>
/// The ambient tint of the world, one colour per frame (docs/adr/ADR-027 D4): a layer of <c>(R, G, B)</c> at opacity <c>A</c> (0 to 255) composited over the
/// world. Alpha 0 means plain daylight.
/// </summary>
/// <param name="R">Red, 0 to 255.</param>
/// <param name="G">Green, 0 to 255.</param>
/// <param name="B">Blue, 0 to 255.</param>
/// <param name="A">Opacity, 0 (none) to 255.</param>
public readonly record struct AmbientTint(int R, int G, int B, int A)
{
    /// <summary>Gets plain daylight: no tint.</summary>
    public static AmbientTint None => default;

    /// <summary>Gets whether anything would be drawn.</summary>
    public bool IsVisible => this.A > 0;
}

/// <summary>
/// The day/night curve: dawn from 05:00 to 07:00, full day to 18:00, dusk from 18:00 to 20:00, night from 20:00 to the day boundary. Keyframes are data;
/// colours between them are interpolated linearly. Rain and storms darken the tint a little; indoors there is no tint except a mild one at night.
/// </summary>
public static class AmbientLightRules
{
    /// <summary>Opacity added by rain and storms.</summary>
    public const int RainDarkening = 28;

    /// <summary>Opacity added by a storm on top of <see cref="RainDarkening"/>.</summary>
    public const int StormDarkening = 14;

    /// <summary>Highest opacity of the night tint indoors.</summary>
    public const int IndoorNightOpacity = 60;

    // Minute of day, then the outdoor tint. The curve wraps: the last key carries on to midnight and the first key is the pre-dawn level.
    private static readonly (int Minute, AmbientTint Tint)[] s_keys =
    [
        (0, new AmbientTint(18, 24, 64, 168)),
        (5 * 60, new AmbientTint(18, 24, 64, 168)),
        (6 * 60, new AmbientTint(255, 150, 90, 70)),
        (7 * 60, new AmbientTint(255, 200, 150, 0)),
        (18 * 60, new AmbientTint(255, 200, 150, 0)),
        (19 * 60, new AmbientTint(255, 120, 60, 80)),
        (20 * 60, new AmbientTint(18, 24, 64, 168)),
        (24 * 60, new AmbientTint(18, 24, 64, 168)),
    ];

    /// <summary>Returns the ambient tint for a time of day.</summary>
    /// <param name="minuteOfDay">Game minute of the day, 0 to 1439 (values outside wrap).</param>
    /// <param name="weather">Today's weather.</param>
    /// <param name="indoors">Whether the player is on an indoor map.</param>
    /// <returns>The tint to composite over the world.</returns>
    public static AmbientTint At(int minuteOfDay, WeatherKind weather, bool indoors)
    {
        int minute = ((minuteOfDay % (24 * 60)) + (24 * 60)) % (24 * 60);
        AmbientTint outdoor = Curve(minute);
        if (indoors)
        {
            // Lit rooms: the night tint is a mild version of the outdoor one, and weather is not felt.
            int night = Math.Min(outdoor.A, IndoorNightOpacity * outdoor.A / s_keys[0].Tint.A);
            return night <= 0 ? AmbientTint.None : outdoor with { A = night };
        }

        int extra = weather switch
        {
            WeatherKind.Rain => RainDarkening,
            WeatherKind.Storm => RainDarkening + StormDarkening,
            _ => 0,
        };

        // Rain greys the sky also at noon: tint the added part with a cool grey when the day itself has none.
        if (extra == 0)
        {
            return outdoor;
        }

        if (outdoor.A == 0)
        {
            return new AmbientTint(40, 52, 80, extra);
        }

        // At dusk and night the sky is already dark: the weather adds only its share of the light that is left. Rain stacked on the full night tint
        // crushed the farm to a dark blue (a rainy night was 196 of 255 opacity, the sprites lost their contrast); now it is a little darker than a clear night.
        int share = extra * (255 - outdoor.A) / 255;
        return outdoor with { A = outdoor.A + share };
    }

    private static AmbientTint Curve(int minute)
    {
        for (int i = 1; i < s_keys.Length; i++)
        {
            (int toMinute, AmbientTint to) = s_keys[i];
            if (minute > toMinute)
            {
                continue;
            }

            (int fromMinute, AmbientTint start) = s_keys[i - 1];
            int span = toMinute - fromMinute;
            if (span <= 0)
            {
                return to;
            }

            int t = minute - fromMinute;

            // Where one end is fully clear its colour does not matter: borrow the other end's, so the fade does not pass through a muddy hue.
            AmbientTint a = start.A == 0 ? to with { A = 0 } : start;
            AmbientTint b = to.A == 0 ? start with { A = 0 } : to;
            return new AmbientTint(Lerp(a.R, b.R, t, span), Lerp(a.G, b.G, t, span), Lerp(a.B, b.B, t, span), Lerp(a.A, b.A, t, span));
        }

        return s_keys[^1].Tint;
    }

    private static int Lerp(int from, int to, int t, int span) => from + ((to - from) * t / span);
}
