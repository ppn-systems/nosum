// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Account;

/// <summary>
/// Length limits for account fields. Constants only: the server enforces them, a client may use
/// them to give early feedback.
/// </summary>
public static class AccountLimits
{
    /// <summary>The only accepted e-mail domain.</summary>
    public const string EmailDomain = "gmail.com";

    /// <summary>Shortest accepted local part (before the @), as Gmail requires.</summary>
    public const int EmailLocalMinLength = 6;

    /// <summary>Longest accepted local part (before the @), as Gmail allows.</summary>
    public const int EmailLocalMaxLength = 30;

    /// <summary>Longest accepted address: local part, @ and <see cref="EmailDomain"/>.</summary>
    public const int EmailMaxLength = EmailLocalMaxLength + 1 + 9;

    /// <summary>Shortest accepted password.</summary>
    public const int PasswordMinLength = 8;

    /// <summary>Longest accepted password (bounds the work a hashing request can cause).</summary>
    public const int PasswordMaxLength = 128;

    /// <summary>Longest accepted BCP-47 language tag.</summary>
    public const int LanguageMaxLength = 35;

    /// <summary>Language used when none is given: Vietnamese.</summary>
    public const string DefaultLanguage = "vi";
}
