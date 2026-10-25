// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Account;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>
/// What the account UI needs from the network. Implemented over the single socket session in
/// <c>Nosum.Wasm.Socket</c>; view models never see the session.
/// Transport failures are reported as <see cref="AccountConnectionException"/>.
/// </summary>
public interface IAccountClient
{
    /// <summary>Creates an account and signs in.</summary>
    /// <param name="username">Wanted username.</param>
    /// <param name="password">Wanted password.</param>
    /// <param name="language">The current UI language tag.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server's status code and, when Ok, the session.</returns>
    Task<AccountReply> RegisterAsync(string username, string password, string language, CancellationToken cancellationToken);

    /// <summary>Signs in with a username and password.</summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server's status code and, when Ok, the session.</returns>
    Task<AccountReply> LoginAsync(string username, string password, CancellationToken cancellationToken);

    /// <summary>Signs in again with a stored session token (used at start-up, not by the form).</summary>
    /// <param name="sessionToken">The token from an earlier sign-in.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server's status code and, when Ok, the session.</returns>
    Task<AccountReply> ResumeAsync(string sessionToken, CancellationToken cancellationToken);
}

/// <summary>A signed-in session as the client sees it.</summary>
/// <param name="PlayerId">The player's id.</param>
/// <param name="Username">The player's username.</param>
/// <param name="SessionToken">Token for <see cref="IAccountClient.ResumeAsync"/>; keep it out of logs.</param>
/// <param name="ExpiresAt">When the token expires.</param>
/// <param name="PreferredLanguage">The stored language tag to apply to the UI.</param>
public sealed record AccountSession(Guid PlayerId, string Username, string SessionToken, DateTimeOffset ExpiresAt, string PreferredLanguage);

/// <summary>The reply to an account request: a status code, plus the session when the code is Ok.</summary>
/// <param name="Status">The server's outcome code.</param>
/// <param name="Session">The session; null unless <paramref name="Status"/> is Ok.</param>
public sealed record AccountReply(AccountStatus Status, AccountSession? Session);
