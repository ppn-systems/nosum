// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Account;

namespace Nosum.Abstractions.Accounts;

/// <summary>Result of an account operation. Only <see cref="Status"/> is set unless it is <see cref="AccountStatus.Ok"/>.</summary>
/// <param name="Status">The outcome code.</param>
/// <param name="PlayerId">The signed-in player.</param>
/// <param name="Username">The player's username.</param>
/// <param name="SessionToken">The session token; empty when the operation issues none.</param>
/// <param name="ExpiresAt">When the session token expires.</param>
/// <param name="PreferredLanguage">The player's stored language tag.</param>
public readonly record struct AccountOutcome(
    AccountStatus Status,
    Guid PlayerId,
    string Username,
    string SessionToken,
    DateTimeOffset ExpiresAt,
    string PreferredLanguage)
{
    /// <summary>Creates a refusal that carries only a status code.</summary>
    /// <param name="status">The refusal code.</param>
    /// <returns>An outcome with empty session fields.</returns>
    public static AccountOutcome Refused(AccountStatus status)
        => new(status, Guid.Empty, string.Empty, string.Empty, default, string.Empty);
}
