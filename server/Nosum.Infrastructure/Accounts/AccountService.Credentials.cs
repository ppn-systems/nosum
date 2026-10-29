// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Players;

namespace Nosum.Infrastructure.Accounts;

public sealed partial class AccountService
{
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
}
