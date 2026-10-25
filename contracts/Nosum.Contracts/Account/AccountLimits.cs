// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Account;

/// <summary>
/// Length limits for account fields. Constants only: the server enforces them, a client may use
/// them to give early feedback.
/// </summary>
public static class AccountLimits
{
    /// <summary>Shortest accepted username.</summary>
    public const int UsernameMinLength = 3;

    /// <summary>Longest accepted username.</summary>
    public const int UsernameMaxLength = 20;

    /// <summary>Shortest accepted password.</summary>
    public const int PasswordMinLength = 8;

    /// <summary>Longest accepted password (bounds the work a hashing request can cause).</summary>
    public const int PasswordMaxLength = 128;

    /// <summary>Longest accepted BCP-47 language tag.</summary>
    public const int LanguageMaxLength = 35;

    /// <summary>Language used when none is given: Vietnamese.</summary>
    public const string DefaultLanguage = "vi";
}
