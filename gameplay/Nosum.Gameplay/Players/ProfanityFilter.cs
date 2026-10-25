// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Linq;
using System.Globalization;
using System.Text;

namespace Nosum.Gameplay.Players;

/// <summary>
/// Finds Vietnamese and English profanity in a nickname (docs/adr/ADR-009 amendments). Pure and shared by the server (which decides) and the
/// client (early feedback). The text is lower-cased, stripped of accents, and read through the usual leet substitutions
/// (0 as o, 1 as i, 3 as e, 4 as a, 5 as s, 7 as t, @ as a, $ as s, ! as i). Then two kinds of term from <see cref="ProfanityTerms"/> are looked
/// for: long words anywhere in the name with its separators removed (so "f u c k" and "Fu_ck" are caught), and short abbreviations only as a whole
/// word of the name (so "dm_99" and "VCL" are caught but "Admin", "Long" and "Scunthorpe" are not). A run of one repeated letter is also read as a
/// single letter ("fuuuck", "DMMM"). Spacing a short abbreviation out ("d m") is read as one word only when the whole name is that word.
/// It is a filter, not a guarantee: determined spellings get through, which is why the list is data and can grow.
/// </summary>
public static class ProfanityFilter
{
    private const int MinCollapsedSubstringLength = 4;

    /// <summary>The most short words <see cref="Mask"/> reads as one spaced-out word.</summary>
    public const int MaxMaskedGroup = 6;

    private const int MaxGroupWordLength = 3;

    private static readonly FrozenSet<string> s_tokens = ProfanityTerms.Tokens.ToFrozenSet(StringComparer.Ordinal);

    // Each substring term next to its form with repeated letters merged (only used when that form is still long enough to be specific).
    private static readonly (string Term, string? Collapsed)[] s_substrings = BuildSubstrings();

    /// <summary>Tells whether a nickname (or any short text) contains a blocked term.</summary>
    /// <param name="text">The text to check; null or empty is clean.</param>
    /// <returns><see langword="true"/> when a blocked term is found.</returns>
    public static bool Contains(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string folded = StripAccents(text);
        string[] words = Split(folded);
        StringBuilder joined = new(folded.Length);
        foreach (string word in words)
        {
            if (IsBlockedWord(word))
            {
                return true;
            }

            _ = joined.Append(word);
        }

        string glued = joined.ToString();
        if (words.Length > 1 && IsBlockedWord(glued))
        {
            return true;
        }

        string leet = BlankInnocent(Leet(glued));
        string merged = Collapse(leet);
        foreach ((string term, string? collapsed) in s_substrings)
        {
            if (leet.Contains(term, StringComparison.Ordinal) || (collapsed is not null && merged.Contains(collapsed, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Masks profanity in chat text (docs/adr/ADR-029 D1): every word that <see cref="Contains"/> flags has its letters and digits replaced by
    /// <c>*</c>, so the length and the punctuation stay. A word is a run of characters between whitespace. Letters spaced out one by one
    /// ("f u c k") are caught as a group of up to <see cref="MaxMaskedGroup"/> short words. Clean text comes back unchanged (the same instance).
    /// It reuses the nickname normalisation, so accents, leet spellings and repeated letters are read the same way.
    /// </summary>
    /// <param name="text">The text to mask; null is empty.</param>
    /// <returns>The masked text.</returns>
    public static string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        System.Collections.Generic.List<(int Start, int Length)> words = [];
        int start = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool space = i == text.Length || char.IsWhiteSpace(text[i]);
            if (!space && start < 0)
            {
                start = i;
            }
            else if (space && start >= 0)
            {
                words.Add((start, i - start));
                start = -1;
            }
        }

        bool[] flagged = new bool[words.Count];
        bool any = false;
        for (int i = 0; i < words.Count; i++)
        {
            flagged[i] = Contains(text.Substring(words[i].Start, words[i].Length));
            any |= flagged[i];
        }

        // Spaced-out letters: only runs of short words, so ordinary sentences are never glued into a match. The longest run is tried first so
        // "f u c k" is masked whole; a longer word swept up at either end is let go again while the rest is still flagged.
        for (int length = MaxMaskedGroup; length >= 2; length--)
        {
            for (int i = 0; i + length <= words.Count; i++)
            {
                if (!IsShortRun(words, i, length) || AnyFlagged(flagged, i, length) || !Contains(Group(text, words, i, length)))
                {
                    continue;
                }

                int from = i;
                int count = length;
                while (count > 2 && words[from].Length > 1 && Contains(Group(text, words, from + 1, count - 1)))
                {
                    from++;
                    count--;
                }

                while (count > 2 && words[from + count - 1].Length > 1 && Contains(Group(text, words, from, count - 1)))
                {
                    count--;
                }

                for (int k = from; k < from + count; k++)
                {
                    flagged[k] = true;
                }

                any = true;
            }
        }

        if (!any)
        {
            return text;
        }

        char[] result = text.ToCharArray();
        for (int i = 0; i < words.Count; i++)
        {
            if (!flagged[i])
            {
                continue;
            }

            for (int k = words[i].Start; k < words[i].Start + words[i].Length; k++)
            {
                if (char.IsLetterOrDigit(result[k]))
                {
                    result[k] = '*';
                }
            }
        }

        return new string(result);
    }

    private static string Group(string text, System.Collections.Generic.List<(int Start, int Length)> words, int from, int count)
        => text.Substring(words[from].Start, words[from + count - 1].Start + words[from + count - 1].Length - words[from].Start);

    private static bool AnyFlagged(bool[] flagged, int from, int count)
    {
        for (int i = from; i < from + count; i++)
        {
            if (flagged[i])
            {
                return true;
            }
        }

        return false;
    }

    // A group of spaced-out letters is made of words of at most three characters.
    private static bool IsShortRun(System.Collections.Generic.List<(int Start, int Length)> words, int from, int count)
    {
        for (int i = from; i < from + count; i++)
        {
            if (words[i].Length > MaxGroupWordLength)
            {
                return false;
            }
        }

        return true;
    }

    // A whole word: as typed (leet undone), with repeats merged, and with digits dropped ("dm2024", "vcl_99" is two words already).
    private static bool IsBlockedWord(string word)
    {
        if (word.Length == 0)
        {
            return false;
        }

        string leet = Leet(word);
        if (s_tokens.Contains(leet) || s_tokens.Contains(Collapse(leet)))
        {
            return true;
        }

        string letters = DropDigits(word);
        return letters.Length > 0 && letters.Length != word.Length && (s_tokens.Contains(Leet(letters)) || s_tokens.Contains(Collapse(Leet(letters))));
    }

    // An innocent word that contains a term is replaced by a separator that matches nothing, so its neighbours cannot join up around it.
    private static string BlankInnocent(string text)
    {
        foreach (string word in ProfanityTerms.Innocent)
        {
            text = text.Replace(word, "#", StringComparison.Ordinal);
        }

        return text;
    }

    private static (string, string?)[] BuildSubstrings()
    {
        (string, string?)[] result = new (string, string?)[ProfanityTerms.Substrings.Count];
        for (int i = 0; i < result.Length; i++)
        {
            string term = ProfanityTerms.Substrings[i];
            string collapsed = Collapse(term);
            result[i] = (term, collapsed.Length >= MinCollapsedSubstringLength ? collapsed : null);
        }

        return result;
    }

    // Lower case, no diacritics (NFD, marks dropped), đ as d.
    private static string StripAccents(string text)
    {
        string decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (c == 'đ')
            {
                _ = builder.Append('d');
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                _ = builder.Append(c);
            }
        }

        return builder.ToString();
    }

    // Words are runs of letters, digits and the leet symbols; anything else (space, underscore, punctuation) separates.
    private static string[] Split(string folded)
    {
        System.Collections.Generic.List<string> words = [];
        StringBuilder current = new();
        foreach (char c in folded)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '@' or '$' or '!' or '+')
            {
                _ = current.Append(c);
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                _ = current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return [.. words];
    }

    private static string Leet(string text)
    {
        StringBuilder builder = new(text.Length);
        foreach (char c in text)
        {
            _ = builder.Append(c switch
            {
                '0' => 'o',
                '1' or '!' => 'i',
                '3' => 'e',
                '4' or '@' => 'a',
                '5' or '$' => 's',
                '7' or '+' => 't',
                _ => c,
            });
        }

        return builder.ToString();
    }

    private static string DropDigits(string text)
    {
        StringBuilder builder = new(text.Length);
        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                _ = builder.Append(c);
            }
        }

        return builder.ToString();
    }

    // "fuuuck" -> "fuck".
    private static string Collapse(string text)
    {
        StringBuilder builder = new(text.Length);
        foreach (char c in text)
        {
            if (builder.Length == 0 || builder[^1] != c)
            {
                _ = builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
