// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Account;

namespace Nosum.Abstractions.Accounts;

/// <summary>Validation rules for account fields. Pure functions; limits come from <see cref="AccountLimits"/>.</summary>
public static class AccountRules
{
    /// <summary>
    /// A username is 3-20 characters: ASCII letters, digits and underscore, starting with a letter.
    /// </summary>
    /// <param name="username">The candidate username.</param>
    /// <returns>True when the username is acceptable.</returns>
    public static bool IsValidUsername(string? username)
    {
        if (username is null
            || username.Length < AccountLimits.UsernameMinLength
            || username.Length > AccountLimits.UsernameMaxLength
            || !IsAsciiLetter(username[0]))
        {
            return false;
        }

        foreach (char c in username)
        {
            if (!IsAsciiLetter(c) && !char.IsAsciiDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A password is 8-128 characters with no other rule (length beats composition).</summary>
    /// <param name="password">The candidate password.</param>
    /// <returns>True when the password is acceptable.</returns>
    public static bool IsValidPassword(string? password)
        => password is not null
            && password.Length >= AccountLimits.PasswordMinLength
            && password.Length <= AccountLimits.PasswordMaxLength;

    /// <summary>
    /// Structural BCP-47 check: subtags of 1-8 ASCII letters or digits separated by '-', the first
    /// 2-8 letters, at most <see cref="AccountLimits.LanguageMaxLength"/> characters. A registry lookup is not done.
    /// </summary>
    /// <param name="tag">The candidate language tag.</param>
    /// <returns>True when the tag is well formed.</returns>
    public static bool IsWellFormedLanguageTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag) || tag.Length > AccountLimits.LanguageMaxLength)
        {
            return false;
        }

        int subtagLength = 0;
        int subtagIndex = 0;
        foreach (char c in tag)
        {
            if (c == '-')
            {
                if (subtagLength == 0)
                {
                    return false;
                }

                if (subtagIndex == 0 && subtagLength < 2)
                {
                    return false;
                }

                subtagLength = 0;
                subtagIndex++;
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(c) || (subtagIndex == 0 && !char.IsAsciiLetter(c)))
            {
                return false;
            }

            subtagLength++;
            if (subtagLength > 8)
            {
                return false;
            }
        }

        return subtagLength > 0 && (subtagIndex > 0 || subtagLength >= 2);
    }

    /// <summary>Lower-cases a username for unique lookups (invariant culture).</summary>
    /// <param name="username">A valid username.</param>
    /// <returns>The normalized form.</returns>
    public static string NormalizeUsername(string username)
    {
        ArgumentNullException.ThrowIfNull(username);
        return username.ToLowerInvariant();
    }

    private static bool IsAsciiLetter(char c) => char.IsAsciiLetter(c);
}
