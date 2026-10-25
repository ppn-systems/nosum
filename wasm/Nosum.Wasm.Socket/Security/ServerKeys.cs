// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Nosum.Wasm.Socket.Security;

/// <summary>Parsing and comparing server public keys (X25519, 32 bytes, hex) for pinning (docs/adr/ADR-032 D1).</summary>
public static class ServerKeys
{
    /// <summary>Length of a key in hex digits.</summary>
    public const int HexLength = 64;

    /// <summary>Most keys accepted in the boot configuration: the current one and the next during a rotation window.</summary>
    public const int MaxPinned = 2;

    /// <summary>The configuration key prefix: <c>Server:PublicKeys:0</c>, <c>Server:PublicKeys:1</c>.</summary>
    public const string ConfigurationPrefix = "Server:PublicKeys:";

    /// <summary>Normalizes a key to upper-case hex.</summary>
    /// <param name="text">The text to read; surrounding white space is ignored.</param>
    /// <param name="keyHex">The normalized key when this returns true.</param>
    /// <returns>True when the text is 64 hex digits.</returns>
    public static bool TryNormalize(string? text, out string keyHex)
    {
        keyHex = string.Empty;
        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length != HexLength)
        {
            return false;
        }

        foreach (char c in trimmed)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        keyHex = trimmed.ToUpperInvariant();
        return true;
    }

    /// <summary>Tells whether two keys are the same.</summary>
    /// <param name="a">One key as hex.</param>
    /// <param name="b">Another key as hex.</param>
    /// <returns>True when both are valid and equal (case-insensitively).</returns>
    public static bool AreSame(string? a, string? b)
        => TryNormalize(a, out string x) && TryNormalize(b, out string y) && string.Equals(x, y, StringComparison.Ordinal);

    /// <summary>
    /// Reads the pinned keys from the boot configuration (<c>Server:PublicKeys:0</c>, <c>Server:PublicKeys:1</c>). Blank entries are skipped. A
    /// key that is not valid hex stops start-up rather than silently weakening the pin.
    /// </summary>
    /// <param name="read">Reads a configuration value by key.</param>
    /// <returns>The pinned keys, normalized; empty when none are configured.</returns>
    /// <exception cref="FormatException">An entry is not a 64-digit hex key, or more than <see cref="MaxPinned"/> are configured.</exception>
    public static string[] FromConfiguration(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        List<string> keys = [];
        for (int index = 0; index <= MaxPinned; index++)
        {
            string? text = read(ConfigurationPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (index >= MaxPinned)
            {
                throw new FormatException("At most " + MaxPinned + " server public keys may be pinned (the current one and the next during a rotation).");
            }

            keys.Add(TryNormalize(text, out string key) ? key : throw new FormatException("Server:PublicKeys:" + index + " is not a 64-digit hex key."));
        }

        return [.. keys];
    }
}
