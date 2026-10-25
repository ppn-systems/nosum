// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Text;
using Nosum.Contracts.Common;

namespace Nosum.Wasm.ViewModels.Settings;

/// <summary>
/// The player's settings on this browser (docs/adr/ADR-033): immutable; every change makes a new value that is stored whole. Written as <c>key=value</c> lines
/// so no serializer is needed and an unknown or damaged line is simply ignored.
/// </summary>
public sealed record ClientSettings
{
    /// <summary>The name of the browser storage entry the settings live in.</summary>
    public const string StorageKey = "nosum.settings.v1";

    /// <summary>Gets the default settings.</summary>
    public static ClientSettings Default { get; } = new();

    /// <summary>Gets the UI language last used on this browser; the sign-in screen starts in it.</summary>
    public GameLanguage Language { get; init; } = GameLanguage.Vietnamese;

    /// <summary>Gets the music volume, 0 to 100 (stored for the audio of M6).</summary>
    public int MusicVolume { get; init; } = 70;

    /// <summary>Gets the sound effects volume, 0 to 100.</summary>
    public int SoundVolume { get; init; } = 70;

    /// <summary>Gets whether all sound is muted.</summary>
    public bool Muted { get; init; }

    /// <summary>Gets the zoom in screen pixels per world pixel: 0 picks it from the window size, otherwise 2 to 6.</summary>
    public int UiScale { get; init; }

    /// <summary>Gets whether names are drawn over the farmers' heads.</summary>
    public bool ShowNames { get; init; } = true;

    /// <summary>Gets whether motion is reduced: no weather particles, no animated UI.</summary>
    public bool ReduceMotion { get; init; }

    /// <summary>Gets whether the high contrast theme is used.</summary>
    public bool HighContrast { get; init; }

    /// <summary>Gets the key bindings.</summary>
    public KeyBindings Bindings { get; init; } = KeyBindings.Default;

    /// <summary>Gets whether the first-run hints are on.</summary>
    public bool TutorialEnabled { get; init; } = true;

    /// <summary>Gets the hint steps that are done, one bit per <see cref="TutorialStep"/>.</summary>
    public int TutorialDone { get; init; }

    /// <summary>Writes the settings.</summary>
    /// <returns>The text to store.</returns>
    public string Serialize()
    {
        StringBuilder text = new();
        Line(text, "lang", ((int)this.Language).ToString(CultureInfo.InvariantCulture));
        Line(text, "music", this.MusicVolume.ToString(CultureInfo.InvariantCulture));
        Line(text, "sound", this.SoundVolume.ToString(CultureInfo.InvariantCulture));
        Line(text, "muted", Flag(this.Muted));
        Line(text, "scale", this.UiScale.ToString(CultureInfo.InvariantCulture));
        Line(text, "names", Flag(this.ShowNames));
        Line(text, "calm", Flag(this.ReduceMotion));
        Line(text, "contrast", Flag(this.HighContrast));
        Line(text, "keys", this.Bindings.Serialize());
        Line(text, "tutorial", Flag(this.TutorialEnabled));
        Line(text, "tutorialDone", this.TutorialDone.ToString(CultureInfo.InvariantCulture));
        return text.ToString();
    }

    /// <summary>Reads settings written by <see cref="Serialize"/>; a missing, unknown or out-of-range entry keeps its default.</summary>
    /// <param name="text">The stored text, or <see langword="null"/> when nothing was stored (or storage is blocked).</param>
    /// <returns>The settings.</returns>
    public static ClientSettings Parse(string? text)
    {
        ClientSettings result = Default;
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int at = line.IndexOf('=', StringComparison.Ordinal);
            if (at <= 0)
            {
                continue;
            }

            string key = line[..at];
            string value = line[(at + 1)..].Trim();
            result = key switch
            {
                "lang" when TryInt(value, out int lang) && Enum.IsDefined((GameLanguage)lang) => result with { Language = (GameLanguage)lang },
                "music" when TryInt(value, out int music) => result with { MusicVolume = Math.Clamp(music, 0, 100) },
                "sound" when TryInt(value, out int sound) => result with { SoundVolume = Math.Clamp(sound, 0, 100) },
                "muted" => result with { Muted = value == "1" },
                "scale" when TryInt(value, out int scale) => result with { UiScale = scale is >= 2 and <= 6 ? scale : 0 },
                "names" => result with { ShowNames = value != "0" },
                "calm" => result with { ReduceMotion = value == "1" },
                "contrast" => result with { HighContrast = value == "1" },
                "keys" => result with { Bindings = KeyBindings.Parse(value) },
                "tutorial" => result with { TutorialEnabled = value != "0" },
                "tutorialDone" when TryInt(value, out int done) => result with { TutorialDone = done & TutorialSteps.AllMask },
                _ => result,
            };
        }

        return result;
    }

    private static string Flag(bool value) => value ? "1" : "0";

    private static void Line(StringBuilder text, string key, string value) => text.Append(key).Append('=').Append(value).Append('\n');

    private static bool TryInt(string value, out int number) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
}
