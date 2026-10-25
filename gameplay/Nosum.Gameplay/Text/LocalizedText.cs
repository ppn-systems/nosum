// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Nosum.Gameplay.Text;

/// <summary>
/// Player-authored text that may show in several languages (docs/adr/ADR-010 D5): a plain string, or a value starting with
/// <c>i18n{</c> that holds a JSON object of <c>{ "&lt;tag&gt;": "&lt;text&gt;" }</c>. The server validates it on save and never
/// rewrites it; clients resolve it for display.
/// </summary>
public static class LocalizedText
{
    /// <summary>The prefix that marks an envelope.</summary>
    public const string EnvelopePrefix = "i18n";

    /// <summary>Longest stored value, envelope included.</summary>
    public const int MaxStoredLength = 256;

    /// <summary>Checks a value: a plain text of <paramref name="minLength"/> to <paramref name="maxLength"/> characters, or a well-formed envelope whose texts are each in that range.</summary>
    /// <param name="value">The value as typed.</param>
    /// <param name="minLength">Shortest accepted text (plain, or each envelope entry).</param>
    /// <param name="maxLength">Longest accepted text (plain, or each envelope entry).</param>
    /// <returns><see langword="true"/> when the value may be stored.</returns>
    public static bool IsValid(string? value, int minLength, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxStoredLength)
        {
            return false;
        }

        if (!value.StartsWith(EnvelopePrefix + "{", StringComparison.Ordinal))
        {
            return IsPlainValid(value, minLength, maxLength);
        }

        if (!TryParse(value, out Dictionary<string, string>? entries) || entries.Count == 0)
        {
            return false;
        }

        foreach (KeyValuePair<string, string> entry in entries)
        {
            if (entry.Key.Length is 0 or > 16 || !IsPlainValid(entry.Value, minLength, maxLength))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Picks the text to show: exact tag, then its base language, then <c>en</c>, then the first entry; never empty for a valid value.</summary>
    /// <param name="value">The stored value.</param>
    /// <param name="tag">The viewer's BCP-47 tag, for example <c>vi</c>.</param>
    /// <returns>The text to display.</returns>
    public static string Resolve(string value, string tag)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(tag);

        if (!value.StartsWith(EnvelopePrefix + "{", StringComparison.Ordinal) || !TryParse(value, out Dictionary<string, string>? entries) || entries.Count == 0)
        {
            return value;
        }

        if (entries.TryGetValue(tag, out string? exact))
        {
            return exact;
        }

        int dash = tag.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0 && entries.TryGetValue(tag[..dash], out string? baseLanguage))
        {
            return baseLanguage;
        }

        if (entries.TryGetValue("en", out string? english))
        {
            return english;
        }

        foreach (KeyValuePair<string, string> first in entries)
        {
            return first.Value;
        }

        return value;
    }

    private static bool IsPlainValid(string text, int minLength, int maxLength)
    {
        string trimmed = text.Trim();
        if (trimmed.Length < minLength || trimmed.Length > maxLength)
        {
            return false;
        }

        foreach (char c in trimmed)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParse(string value, out Dictionary<string, string> entries)
    {
        entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using JsonDocument document = JsonDocument.Parse(value[EnvelopePrefix.Length..]);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString() is not { } text)
                {
                    return false;
                }

                entries[property.Name] = text;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
