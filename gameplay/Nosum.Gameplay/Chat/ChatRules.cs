// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Text;
using Nosum.Gameplay.Players;

namespace Nosum.Gameplay.Chat;

/// <summary>
/// The rules for player-authored chat text (docs/adr/ADR-029 D1-D3). Pure and shared by the server (which decides) and the client
/// (which limits the input box). Text is the one thing that travels the wire as typed, so the server cleans it before anyone sees it:
/// normalised to NFC, every control, format (bidi override, zero width) and separator character dropped, tabs and line breaks read as
/// a space, runs of spaces collapsed, at most <see cref="MaxLength"/> characters, then profanity masked (<see cref="ProfanityFilter.Mask"/>).
/// </summary>
public static class ChatRules
{
    /// <summary>Longest chat message, in characters (Unicode scalar values) after cleaning.</summary>
    public const int MaxLength = 160;

    /// <summary>How many messages a player is remembered to have received, as context for a report (docs/adr/ADR-029 D3).</summary>
    public const int ReportContextLines = 20;

    /// <summary>The most players a block list holds.</summary>
    public const int MaxBlocked = 100;

    /// <summary>Cleans typed text: NFC, no control characters, collapsed spaces, at most <see cref="MaxLength"/> characters. Profanity is not touched.</summary>
    /// <param name="text">The text as typed; null is empty.</param>
    /// <returns>The cleaned text; empty when nothing printable is left.</returns>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string normalized;
        try
        {
            normalized = text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            // A lone surrogate cannot be normalised: nobody typed it on purpose.
            return string.Empty;
        }

        StringBuilder builder = new(Math.Min(normalized.Length, MaxLength * 2));
        bool pendingSpace = false;
        int characters = 0;
        foreach (Rune rune in normalized.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (IsDropped(rune))
            {
                continue;
            }

            int needed = pendingSpace ? 2 : 1;
            if (characters + needed > MaxLength)
            {
                break;
            }

            if (pendingSpace)
            {
                _ = builder.Append(' ');
                characters++;
                pendingSpace = false;
            }

            _ = builder.Append(rune.ToString());
            characters++;
        }

        return builder.ToString();
    }

    /// <summary>Cleans typed text and masks profanity: what other players are shown.</summary>
    /// <param name="text">The text as typed; null is empty.</param>
    /// <returns>The text to deliver; empty when there is nothing to send.</returns>
    public static string Prepare(string? text) => ProfanityFilter.Mask(Sanitize(text));

    private static bool IsDropped(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.Control
        or UnicodeCategory.Format
        or UnicodeCategory.LineSeparator
        or UnicodeCategory.ParagraphSeparator
        or UnicodeCategory.Surrogate
        or UnicodeCategory.PrivateUse
        or UnicodeCategory.OtherNotAssigned;
}
