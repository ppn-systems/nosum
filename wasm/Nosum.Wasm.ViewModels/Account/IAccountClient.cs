// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>
/// What the account UI needs from the network. Implemented over the single socket session in
/// <c>Nosum.Wasm.Socket</c>; view models never see the session.
/// Transport failures are reported as <see cref="AccountConnectionException"/>.
/// </summary>
public interface IAccountClient
{
    /// <summary>Signs in with a Google or Microsoft ID token; the server creates the account on first use.</summary>
    /// <param name="provider">The provider that issued <paramref name="idToken"/>.</param>
    /// <param name="idToken">The provider's ID token. Never logged.</param>
    /// <param name="language">The current UI language, stored when the account is created.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server's status code and, when Ok, the session.</returns>
    Task<AccountReply> SignInWithExternalAsync(ExternalProvider provider, string idToken, GameLanguage language, CancellationToken cancellationToken);

    /// <summary>Starts playing as a guest: the server creates a guest player and a session.</summary>
    /// <param name="language">The current UI language.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server's status code and, when Ok, the guest session.</returns>
    Task<AccountReply> StartGuestAsync(GameLanguage language, CancellationToken cancellationToken);

    /// <summary>Turns the signed-in guest into a registered player by linking a Google or Microsoft identity (same player, new session token).</summary>
    /// <param name="provider">The provider that issued <paramref name="idToken"/>.</param>
    /// <param name="idToken">The provider's ID token. Never logged.</param>
    /// <param name="nickname">Wanted display nickname, replacing the guest's neutral one.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server's status code and, when Ok, the new session.</returns>
    Task<AccountReply> LinkExternalIdentityAsync(ExternalProvider provider, string idToken, string nickname, CancellationToken cancellationToken);

    /// <summary>Changes the signed-in player's nickname (allowed once every 24 hours).</summary>
    /// <param name="nickname">The wanted nickname.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server's status code, the nickname after the request and when the next change is allowed.</returns>
    Task<NicknameReply> SetNicknameAsync(string nickname, CancellationToken cancellationToken);

    /// <summary>Stores the signed-in player's language on the server so it applies the next time the account signs in.</summary>
    /// <param name="language">The wanted language.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server's status code (Ok or InvalidLanguage).</returns>
    Task<AccountStatus> SetLanguageAsync(GameLanguage language, CancellationToken cancellationToken);

    /// <summary>Ends the session on the server; its token can no longer resume.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the server has signed the connection out.</returns>
    Task LogoutAsync(CancellationToken cancellationToken);

    /// <summary>Signs in again with a stored session token (used at start-up, not by the form).</summary>
    /// <param name="sessionToken">The token from an earlier sign-in.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server's status code and, when Ok, the session.</returns>
    Task<AccountReply> ResumeAsync(string sessionToken, CancellationToken cancellationToken);
}

/// <summary>A signed-in session as the client sees it.</summary>
/// <param name="PlayerId">The player's id.</param>
/// <param name="Email">The e-mail the identity provider reported, for display only; empty for a guest or when none was sent.</param>
/// <param name="SessionToken">Token for <see cref="IAccountClient.ResumeAsync"/>; keep it out of logs.</param>
/// <param name="ExpiresAt">When the token expires.</param>
/// <param name="Language">The stored language to apply to the UI.</param>
/// <param name="IsGuest">Whether the player is still a guest.</param>
/// <param name="RegistrationPromptUnlocked">Whether the game has unlocked the invitation to register.</param>
/// <param name="Nickname">The nickname as the server sent it (a guest's neutral <c>guest-1234</c> is localized by <c>NicknameFormatter</c>).</param>
public sealed record AccountSession(
    Guid PlayerId,
    string Email,
    string SessionToken,
    DateTimeOffset ExpiresAt,
    GameLanguage Language,
    bool IsGuest,
    bool RegistrationPromptUnlocked,
    string Nickname);

/// <summary>The reply to an account request: a status code, plus the session when the code is Ok.</summary>
/// <param name="Status">The server's outcome code.</param>
/// <param name="Session">The session; null unless <paramref name="Status"/> is Ok.</param>
public sealed record AccountReply(AccountStatus Status, AccountSession? Session);

/// <summary>The reply to a nickname change.</summary>
/// <param name="Status">The server's outcome code.</param>
/// <param name="Nickname">The nickname after the request.</param>
/// <param name="NextChangeAt">When the next change is allowed; null when one is allowed now.</param>
public sealed record NicknameReply(AccountStatus Status, string Nickname, DateTimeOffset? NextChangeAt);
