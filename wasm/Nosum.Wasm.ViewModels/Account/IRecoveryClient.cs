// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Account;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>Account recovery and password change requests (docs/adr/ADR-032 D4, D5). Kept apart from <see cref="IAccountClient"/> so the sign-in client does not grow.</summary>
public interface IRecoveryClient
{
    /// <summary>Asks the server to e-mail a one-time code to the account at this address. The answer is Ok whether or not the address has an account.</summary>
    /// <param name="email">The account e-mail.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Ok, <see cref="AccountStatus.InvalidEmail"/> or <see cref="AccountStatus.TooManyRequests"/>.</returns>
    /// <exception cref="AccountConnectionException">The connection is not ready or the request timed out.</exception>
    Task<AccountStatus> RequestResetAsync(string email, CancellationToken cancellationToken);

    /// <summary>Sets a new password with the e-mailed code. Every session of the account ends.</summary>
    /// <param name="email">The account e-mail.</param>
    /// <param name="code">The 8-digit code.</param>
    /// <param name="newPassword">The new password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Ok, <see cref="AccountStatus.InvalidResetCode"/>, <see cref="AccountStatus.InvalidPassword"/> or <see cref="AccountStatus.TooManyRequests"/>.</returns>
    /// <exception cref="AccountConnectionException">The connection is not ready or the request timed out.</exception>
    Task<AccountStatus> ConfirmResetAsync(string email, string code, string newPassword, CancellationToken cancellationToken);

    /// <summary>Changes the signed-in player's password; every other session of the account ends.</summary>
    /// <param name="currentPassword">The current password.</param>
    /// <param name="newPassword">The new password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Ok, <see cref="AccountStatus.InvalidCredentials"/> for a wrong current password, or <see cref="AccountStatus.InvalidPassword"/>.</returns>
    /// <exception cref="AccountConnectionException">The connection is not ready or the request timed out.</exception>
    Task<AccountStatus> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken);
}
