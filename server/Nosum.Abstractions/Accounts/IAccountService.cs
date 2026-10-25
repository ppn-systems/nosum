// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Account;

namespace Nosum.Abstractions.Accounts;

/// <summary>Player accounts: register, sign in, resume, preferences. Handlers call this; they never touch the database.</summary>
public interface IAccountService
{
    /// <summary>Creates an account and issues a session token.</summary>
    /// <param name="username">Wanted username.</param>
    /// <param name="password">Wanted password.</param>
    /// <param name="language">Preferred language tag; empty selects the default.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok with a session, or a refusal code.</returns>
    Task<AccountOutcome> RegisterAsync(string username, string password, string language, CancellationToken cancellationToken);

    /// <summary>Checks credentials and issues a session token.</summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok with a session, or <see cref="AccountStatus.InvalidCredentials"/>.</returns>
    Task<AccountOutcome> LoginAsync(string username, string password, CancellationToken cancellationToken);

    /// <summary>Signs a player in with a token from an earlier register or login. The token stays valid until it expires.</summary>
    /// <param name="sessionToken">The token the client holds.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok with the player (the same token is returned), or <see cref="AccountStatus.InvalidToken"/>.</returns>
    Task<AccountOutcome> ResumeAsync(string sessionToken, CancellationToken cancellationToken);

    /// <summary>Stores a player's preferred language.</summary>
    /// <param name="playerId">The player, taken from the connection identity.</param>
    /// <param name="language">A BCP-47 language tag.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok or <see cref="AccountStatus.InvalidLanguage"/>.</returns>
    Task<AccountStatus> SetLanguageAsync(Guid playerId, string language, CancellationToken cancellationToken);
}
