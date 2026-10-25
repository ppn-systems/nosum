// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Nosum.Gameplay.Players;

/// <summary>Why a nickname was refused (docs/adr/ADR-009 amendments).</summary>
public enum NicknameVerdict
{
    /// <summary>The nickname is allowed.</summary>
    Ok = 0,

    /// <summary>Too short, too long, or it contains a character that is not allowed.</summary>
    Invalid = 1,

    /// <summary>The nickname is on the reserved list, or imitates a guest name.</summary>
    Reserved = 2,

    /// <summary>The nickname contains profanity or a slur (<see cref="ProfanityFilter"/>).</summary>
    Profane = 3,
}

/// <summary>
/// The rules for a player's display nickname. Pure, shared by the server (which decides) and the client (which gives live feedback).
/// A nickname is 3 to 16 characters after trimming: unaccented ASCII letters (A-Z, a-z), ASCII digits, and single spaces or underscores
/// between them; at least one letter; not on the reserved list; free of profanity (<see cref="ProfanityFilter"/>). Accented letters are refused,
/// which also rules out Cyrillic and Greek look-alikes. Nicknames stored before this rule (docs/adr/ADR-009 amendments, 2026-10-25) are not
/// re-checked: they stay valid until their owner changes them, and only a new or changed nickname must pass <see cref="Validate"/>. Uniqueness is case-insensitive on
/// <see cref="Key"/>. Guests carry a neutral name <c>guest-1234</c> that no player can type (a hyphen is not allowed), which the client
/// renders with a localized prefix (docs/adr/ADR-010: no display text on the wire).
/// </summary>
public static class NicknameRules
{
    /// <summary>Shortest nickname.</summary>
    public const int MinLength = 3;

    /// <summary>Longest nickname.</summary>
    public const int MaxLength = 16;

    /// <summary>Hours a player must wait between two nickname changes.</summary>
    public const int RenameCooldownHours = 24;

    /// <summary>The neutral prefix of a guest's nickname, <c>guest-</c> followed by digits.</summary>
    public const string GuestPrefix = "guest-";

    // Compared after Fold(): lower case, no diacritics, no separators, no digits. "Admin_2" and "Ádmin" both fold to "admin".
    private static readonly HashSet<string> s_reserved = new(StringComparer.Ordinal)
    {
        "admin",
        "administrator",
        "moderator",
        "mod",
        "gm",
        "gamemaster",
        "system",
        "server",
        "staff",
        "support",
        "owner",
        "root",
        "nosum",
        "guest",
        "khach",
        "null",
        "undefined",
        "quantri",
        "quantrivien",
        "hethong",
        "kiemduyet",
        "kiemduyetvien",
    };

    /// <summary>Normalizes a typed nickname: trimmed, NFC (stored legacy names are NFC). Does not check the rules.</summary>
    /// <param name="typed">What the player typed.</param>
    /// <returns>The normalized text (empty for null).</returns>
    public static string Normalize(string? typed)
        => string.IsNullOrWhiteSpace(typed) ? string.Empty : typed.Trim().Normalize(NormalizationForm.FormC);

    /// <summary>Checks a nickname. The text is normalized (trimmed) first. Order of the checks: shape, reserved names, profanity.</summary>
    /// <param name="typed">What the player typed.</param>
    /// <returns><see cref="NicknameVerdict.Ok"/> or the reason.</returns>
    public static NicknameVerdict Validate(string? typed)
    {
        string name = Normalize(typed);
        if (name.Length < MinLength || name.Length > MaxLength)
        {
            return NicknameVerdict.Invalid;
        }

        bool hasLetter = false;
        bool previousSeparator = true; // a separator may not lead
        foreach (char c in name)
        {
            if (c is ' ' or '_')
            {
                if (previousSeparator)
                {
                    return NicknameVerdict.Invalid;
                }

                previousSeparator = true;
                continue;
            }

            if (char.IsAsciiLetter(c))
            {
                hasLetter = true;
            }
            else if (!char.IsAsciiDigit(c))
            {
                return NicknameVerdict.Invalid;
            }

            previousSeparator = false;
        }

        if (!hasLetter || previousSeparator)
        {
            return NicknameVerdict.Invalid;
        }

        if (s_reserved.Contains(Fold(name)))
        {
            return NicknameVerdict.Reserved;
        }

        return ProfanityFilter.Contains(name) ? NicknameVerdict.Profane : NicknameVerdict.Ok;
    }

    /// <summary>The key two nicknames are compared by: NFC, lower case, underscore read as a space. Two names with the same key are the same name.</summary>
    /// <param name="nickname">A nickname (valid or a guest name).</param>
    /// <returns>The unique key.</returns>
    public static string Key(string nickname)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        return Normalize(nickname).ToLowerInvariant().Replace('_', ' ');
    }

    /// <summary>Builds a guest's neutral nickname.</summary>
    /// <param name="number">The number, at least four digits wide.</param>
    /// <returns>For example <c>guest-0042</c>.</returns>
    public static string GuestName(int number)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        return GuestPrefix + number.ToString("D4", CultureInfo.InvariantCulture);
    }

    /// <summary>Tells whether a nickname is a guest's neutral one and returns its number text.</summary>
    /// <param name="nickname">A nickname as received from the server.</param>
    /// <param name="number">The digits after the prefix when it is a guest name.</param>
    /// <returns><see langword="true"/> for <c>guest-</c> followed by digits only.</returns>
    public static bool IsGuestName(string? nickname, out string number)
    {
        number = string.Empty;
        if (nickname is null || nickname.Length <= GuestPrefix.Length || !nickname.StartsWith(GuestPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> digits = nickname.AsSpan(GuestPrefix.Length);
        foreach (char c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        number = new string(digits);
        return true;
    }

    // Lower case without diacritics, separators or digits, so look-alikes of a reserved word fold onto it.
    private static string Fold(string name)
    {
        string decomposed = name.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (c == 'đ')
            {
                _ = builder.Append('d');
            }
            else if (char.IsLetter(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                _ = builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
