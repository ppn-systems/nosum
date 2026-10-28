// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Email;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Players;

namespace Nosum.Infrastructure.Accounts;

public sealed partial class AccountService
{
    private const int ResetCodeDigits = 8;

    /// <inheritdoc/>
    public async Task<AccountStatus> RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        if (!AccountRules.IsValidEmail(email))
        {
            return AccountStatus.InvalidEmail;
        }

        string normalized = AccountRules.NormalizeEmail(email);
        string code = NewResetCode();
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>()
                .FirstOrDefaultAsync(p => p.NormalizedEmail == normalized && !p.IsGuest, cancellationToken).ConfigureAwait(false);
            if (player is null)
            {
                // Pays for the same hash a real account does, so the answer time does not tell which addresses exist.
                _ = await Task.Run(() => _hasher.Hash(code), cancellationToken).ConfigureAwait(false);
                return AccountStatus.Ok;
            }

            using (await _credentialLocks.EnterAsync(player.Id, cancellationToken).ConfigureAwait(false))
            {
                DateTimeOffset now = _time.GetUtcNow();
                DateTimeOffset since = now.AddHours(-1);
                int recent = await unit.Repository<PasswordResetCode>().CountAsync(c => c.PlayerId == player.Id && c.CreatedAt > since, cancellationToken).ConfigureAwait(false);
                if (recent >= ResetRequestsPerHour)
                {
                    // Silent on purpose: a refusal would reveal that the address has an account.
                    return AccountStatus.Ok;
                }

                // Only the newest code works.
                foreach (PasswordResetCode open in await unit.Repository<PasswordResetCode>()
                    .ListAsync(c => c.PlayerId == player.Id && c.ConsumedAt == null, cancellationToken).ConfigureAwait(false))
                {
                    open.ConsumedAt = now;
                }

                string hash = await Task.Run(() => _hasher.Hash(code), cancellationToken).ConfigureAwait(false);
                unit.Repository<PasswordResetCode>().Add(new PasswordResetCode
                {
                    PlayerId = player.Id,
                    CodeHash = hash,
                    CreatedAt = now,
                    ExpiresAt = now + ResetCodeLifetime,
                });
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await this.SendResetMailAsync(player.Email ?? email, player.Language, code, cancellationToken).ConfigureAwait(false);
                return AccountStatus.Ok;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<PasswordResetOutcome> ConfirmPasswordResetAsync(string email, string code, string newPassword, CancellationToken cancellationToken)
    {
        if (!AccountRules.IsValidEmail(email) || !IsResetCodeShape(code))
        {
            return new PasswordResetOutcome(AccountStatus.InvalidResetCode, Guid.Empty);
        }

        if (!AccountRules.IsValidPassword(newPassword))
        {
            return new PasswordResetOutcome(AccountStatus.InvalidPassword, Guid.Empty);
        }

        string normalized = AccountRules.NormalizeEmail(email);
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>()
                .FirstOrDefaultAsync(p => p.NormalizedEmail == normalized && !p.IsGuest, cancellationToken).ConfigureAwait(false);
            if (player is null)
            {
                _ = await Task.Run(() => _hasher.Verify(code, _decoyHash.Value), cancellationToken).ConfigureAwait(false);
                return new PasswordResetOutcome(AccountStatus.InvalidResetCode, Guid.Empty);
            }

            // One try at a time per account, so the attempt limit and the single use hold under concurrent requests.
            using (await _credentialLocks.EnterAsync(player.Id, cancellationToken).ConfigureAwait(false))
            {
                DateTimeOffset now = _time.GetUtcNow();
                List<PasswordResetCode> open = await unit.Repository<PasswordResetCode>()
                    .ListAsync(c => c.PlayerId == player.Id && c.ConsumedAt == null && c.ExpiresAt > now && c.Attempts < ResetMaxAttempts, cancellationToken).ConfigureAwait(false);
                PasswordResetCode? current = null;
                foreach (PasswordResetCode candidate in open)
                {
                    if (current is null || candidate.CreatedAt > current.CreatedAt)
                    {
                        current = candidate;
                    }
                }

                string storedHash = current?.CodeHash ?? _decoyHash.Value;
                bool matches = await Task.Run(() => _hasher.Verify(code, storedHash), cancellationToken).ConfigureAwait(false);
                if (current is null || !matches)
                {
                    if (current is not null)
                    {
                        current.Attempts++;
                        _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }

                    return new PasswordResetOutcome(AccountStatus.InvalidResetCode, Guid.Empty);
                }

                string hash = await Task.Run(() => _hasher.Hash(newPassword), cancellationToken).ConfigureAwait(false);
                foreach (PasswordResetCode used in open)
                {
                    used.ConsumedAt = now;
                }

                player.PasswordHash = hash;
                player.LastActiveAt = now;
                await RevokeSessionsAsync(unit, player.Id, now, keep: null, cancellationToken).ConfigureAwait(false);
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new PasswordResetOutcome(AccountStatus.Ok, player.Id);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<AccountStatus> ChangePasswordAsync(Guid playerId, Guid sessionId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        using (await _credentialLocks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                Player? player = await unit.Repository<Player>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
                if (player is null)
                {
                    return AccountStatus.InvalidToken;
                }

                if (player.IsGuest || player.PasswordHash is null)
                {
                    return AccountStatus.NotGuest;
                }

                if (!AccountRules.IsValidPassword(newPassword))
                {
                    return AccountStatus.InvalidPassword;
                }

                if (string.IsNullOrEmpty(currentPassword) || currentPassword.Length > AccountLimits.PasswordMaxLength
                    || !await Task.Run(() => _hasher.Verify(currentPassword, player.PasswordHash), cancellationToken).ConfigureAwait(false))
                {
                    return AccountStatus.InvalidCredentials;
                }

                DateTimeOffset now = _time.GetUtcNow();
                player.PasswordHash = await Task.Run(() => _hasher.Hash(newPassword), cancellationToken).ConfigureAwait(false);
                await RevokeSessionsAsync(unit, playerId, now, sessionId, cancellationToken).ConfigureAwait(false);
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return AccountStatus.Ok;
            }
        }
    }

    private static string NewResetCode()
        => RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D" + ResetCodeDigits.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsResetCodeShape(string? code)
    {
        if (code is null || code.Length != ResetCodeDigits)
        {
            return false;
        }

        foreach (char c in code)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    // Revokes every live token of a player except the one in use.
    private static async Task RevokeSessionsAsync(IUnitOfWork unit, Guid playerId, DateTimeOffset now, Guid? keep, CancellationToken cancellationToken)
    {
        foreach (PlayerSessionToken token in await unit.Repository<PlayerSessionToken>()
            .ListAsync(t => t.PlayerId == playerId && t.RevokedAt == null, cancellationToken).ConfigureAwait(false))
        {
            if (keep is null || token.Id != keep.Value)
            {
                token.RevokedAt = now;
            }
        }
    }

    // Makes room for one more token: the oldest live ones beyond the cap are revoked (the token being replaced is spared the count's worry: it is simply among them).
    private static async Task EnforceSessionCapAsync(IUnitOfWork unit, Guid playerId, DateTimeOffset now, Guid? keep, CancellationToken cancellationToken)
    {
        List<PlayerSessionToken> live = await unit.Repository<PlayerSessionToken>()
            .ListAsync(t => t.PlayerId == playerId && t.RevokedAt == null && t.ExpiresAt > now, cancellationToken).ConfigureAwait(false);
        int excess = live.Count - (MaxSessionsPerAccount - 1);
        if (excess <= 0)
        {
            return;
        }

        live.Sort(static (a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
        foreach (PlayerSessionToken token in live)
        {
            if (excess <= 0)
            {
                break;
            }

            if (keep is not null && token.Id == keep.Value)
            {
                continue;
            }

            token.RevokedAt = now;
            excess--;
        }
    }

    // The mail is best effort and never lets a provider outage reveal which addresses exist: a failure is logged without the code and the request still answers Ok.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A mail provider failure must not change the answer to the caller; it is logged instead.")]
    private async Task SendResetMailAsync(string to, GameLanguage language, string code, CancellationToken cancellationToken)
    {
        if (_email is null)
        {
            return;
        }

        try
        {
            await _email.SendAsync(new EmailMessage(EmailKind.PasswordReset, to, language, code), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Password reset mail failed: " + ex.GetType().Name).ConfigureAwait(false);
        }
    }
}
