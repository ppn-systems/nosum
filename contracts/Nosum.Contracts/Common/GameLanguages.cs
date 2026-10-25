// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Common;

/// <summary>
/// The one mapping between <see cref="GameLanguage"/> and BCP-47 tags. BCP-47 is used only where a
/// tag is required: .resx culture names, <c>CultureInfo</c>, and the <c>i18n{}</c> content envelope.
/// </summary>
public static class GameLanguages
{
    /// <summary>The language used when none is chosen.</summary>
    public const GameLanguage Default = GameLanguage.Vietnamese;

    /// <summary>Tells whether a value is one of the defined languages (a byte from the wire can be anything).</summary>
    /// <param name="language">The value to check.</param>
    /// <returns>True for <see cref="GameLanguage.Unspecified"/>, <see cref="GameLanguage.Vietnamese"/> and <see cref="GameLanguage.English"/>.</returns>
    public static bool IsDefined(GameLanguage language) => language <= GameLanguage.English;

    /// <summary>Replaces <see cref="GameLanguage.Unspecified"/> by the default.</summary>
    /// <param name="language">The language, possibly unspecified.</param>
    /// <returns>A concrete language.</returns>
    public static GameLanguage Resolve(GameLanguage language) => language == GameLanguage.Unspecified ? Default : language;

    /// <summary>Maps a language to its BCP-47 tag.</summary>
    /// <param name="language">The language; <see cref="GameLanguage.Unspecified"/> maps like the default.</param>
    /// <returns>The tag, for example <c>vi</c> or <c>en</c>.</returns>
    public static string ToTag(GameLanguage language) => Resolve(language) switch
    {
        GameLanguage.English => "en",
        _ => "vi",
    };

    /// <summary>Maps a BCP-47 tag (or a culture name such as <c>en-US</c>) to a language by its primary subtag.</summary>
    /// <param name="tag">The tag, or null.</param>
    /// <returns>The language, or <see cref="GameLanguage.Unspecified"/> when the tag is empty or not supported.</returns>
    public static GameLanguage FromTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return GameLanguage.Unspecified;
        }

        int dash = tag.IndexOf('-', System.StringComparison.Ordinal);
        string primary = dash < 0 ? tag : tag[..dash];
        if (string.Equals(primary, "vi", System.StringComparison.OrdinalIgnoreCase))
        {
            return GameLanguage.Vietnamese;
        }

        return string.Equals(primary, "en", System.StringComparison.OrdinalIgnoreCase) ? GameLanguage.English : GameLanguage.Unspecified;
    }
}
