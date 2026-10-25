// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;

namespace Nosum.Abstractions.Accounts;

/// <summary>Player accounts: guest, register, sign in, resume, upgrade, logout, preferences. Handlers call this; they never touch the database.</summary>
public interface IAccountService
{
    /// <summary>Creates a registered account and issues a session token.</summary>
    /// <param name="email">Wanted login, a @gmail.com address.</param>
    /// <param name="password">Wanted password.</param>
    /// <param name="nickname">Wanted display nickname (<c>NicknameRules</c>); must be free.</param>
    /// <param name="language">Preferred language; Unspecified selects Vietnamese.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok with a session, or a refusal code.</returns>
    Task<AccountOutcome> RegisterAsync(string email, string password, string nickname, GameLanguage language, CancellationToken cancellationToken);

    /// <summary>Creates a guest player (no e-mail, no password, a neutral nickname such as <c>guest-1234</c>) and issues a session token so the guest can resume.</summary>
    /// <param name="language">Preferred language; Unspecified selects Vietnamese.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok with a guest session, or <see cref="AccountStatus.InvalidLanguage"/>.</returns>
    Task<AccountOutcome> StartGuestAsync(GameLanguage language, CancellationToken cancellationToken);

    /// <summary>
    /// Turns a guest into a registered player in place: the player id, state and everything attached to
    /// it stay. The guest's session token is revoked and a new one issued.
    /// </summary>
    /// <param name="playerId">The signed-in player, taken from the connection identity.</param>
    /// <param name="sessionId">The session being replaced, taken from the connection identity.</param>
    /// <param name="email">Wanted login, a @gmail.com address.</param>
    /// <param name="password">Wanted password.</param>
    /// <param name="nickname">Wanted display nickname, replacing the guest's neutral one.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok with the new session, <see cref="AccountStatus.NotGuest"/>, or a validation refusal.</returns>
    Task<AccountOutcome> UpgradeGuestAsync(Guid playerId, Guid sessionId, string email, string password, string nickname, CancellationToken cancellationToken);

    /// <summary>Checks credentials and issues a session token.</summary>
    /// <param name="email">The login e-mail.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok with a session, or <see cref="AccountStatus.InvalidCredentials"/>.</returns>
    Task<AccountOutcome> LoginAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>Signs a player in with a token from an earlier sign-in. The token stays valid until it expires or is revoked.</summary>
    /// <param name="sessionToken">The token the client holds.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok with the player (the same token is returned), or <see cref="AccountStatus.InvalidToken"/>.</returns>
    Task<AccountOutcome> ResumeAsync(string sessionToken, CancellationToken cancellationToken);

    /// <summary>Revokes a session so its token can no longer resume.</summary>
    /// <param name="sessionId">The session from the connection identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the session is revoked.</returns>
    Task LogoutAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Stores a player's preferred language.</summary>
    /// <param name="playerId">The player, taken from the connection identity.</param>
    /// <param name="language">The new language; Unspecified selects Vietnamese.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok or <see cref="AccountStatus.InvalidLanguage"/>.</returns>
    Task<AccountStatus> SetLanguageAsync(Guid playerId, GameLanguage language, CancellationToken cancellationToken);

    /// <summary>
    /// Changes a player's nickname: validated (<c>NicknameRules</c>), unique case-insensitively, and allowed once every 24 hours
    /// (the first change is free). Asking for the nickname the player already has changes nothing and does not start the wait.
    /// </summary>
    /// <param name="playerId">The player, taken from the connection identity.</param>
    /// <param name="nickname">The wanted nickname.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok, or <see cref="AccountStatus.InvalidNickname"/>, <see cref="AccountStatus.NicknameReserved"/>, <see cref="AccountStatus.NicknameTaken"/>, <see cref="AccountStatus.NicknameTooSoon"/>.</returns>
    Task<NicknameOutcome> SetNicknameAsync(Guid playerId, string nickname, CancellationToken cancellationToken);
}
