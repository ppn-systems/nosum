// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Account;

namespace Nosum.Abstractions.Accounts;

/// <summary>Validation rules for account fields. Pure functions; limits come from <see cref="AccountLimits"/>.</summary>
public static class AccountRules
{
    /// <summary>
    /// A login is a <c>@gmail.com</c> address (domain case-insensitive). The local part is 6-30 characters:
    /// ASCII letters, digits and dots, starting and ending with a letter or digit, no two dots in a row
    /// (Gmail's own rules; <c>+</c> tags are refused so one mailbox cannot become many logins).
    /// </summary>
    /// <param name="email">The candidate address, as typed.</param>
    /// <returns>True when the address is acceptable.</returns>
    public static bool IsValidEmail(string? email)
    {
        const string domainSuffix = "@" + AccountLimits.EmailDomain;
        if (email is null
            || email.Length > AccountLimits.EmailMaxLength
            || !email.EndsWith(domainSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> local = email.AsSpan(0, email.Length - domainSuffix.Length);
        if (local.Length < AccountLimits.EmailLocalMinLength
            || local.Length > AccountLimits.EmailLocalMaxLength
            || !char.IsAsciiLetterOrDigit(local[0])
            || !char.IsAsciiLetterOrDigit(local[^1]))
        {
            return false;
        }

        for (int i = 0; i < local.Length; i++)
        {
            char c = local[i];
            if (c == '.' ? local[i - 1] == '.' : !char.IsAsciiLetterOrDigit(c))
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

    /// <summary>Lower-cases an e-mail address for unique lookups (invariant culture).</summary>
    /// <param name="email">A valid address.</param>
    /// <returns>The normalized form.</returns>
    public static string NormalizeEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.ToLowerInvariant();
    }
}
