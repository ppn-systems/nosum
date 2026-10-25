// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;

namespace Nosum.Abstractions.Accounts;

/// <summary>Result of an account operation. Only <see cref="Status"/> is set unless it is <see cref="AccountStatus.Ok"/>.</summary>
/// <param name="Status">The outcome code.</param>
/// <param name="PlayerId">The signed-in player.</param>
/// <param name="Email">The login e-mail in lower case; empty for a guest.</param>
/// <param name="SessionToken">The session token; empty when the operation issues none.</param>
/// <param name="SessionId">The id of the stored session (what logout revokes); empty when none.</param>
/// <param name="ExpiresAt">When the session token expires.</param>
/// <param name="Language">The player's stored language.</param>
/// <param name="IsGuest">Whether the player is still a guest.</param>
/// <param name="RegistrationPromptUnlocked">Whether the invitation to register has been unlocked.</param>
public readonly record struct AccountOutcome(
    AccountStatus Status,
    Guid PlayerId,
    string Email,
    string SessionToken,
    Guid SessionId,
    DateTimeOffset ExpiresAt,
    GameLanguage Language,
    bool IsGuest,
    bool RegistrationPromptUnlocked)
{
    /// <summary>Creates a refusal that carries only a status code.</summary>
    /// <param name="status">The refusal code.</param>
    /// <returns>An outcome with empty session fields.</returns>
    public static AccountOutcome Refused(AccountStatus status)
        => new(status, Guid.Empty, string.Empty, string.Empty, Guid.Empty, default, GameLanguage.Unspecified, false, false);
}
