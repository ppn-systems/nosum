// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Security;
using Nosum.Contracts.Account;
using Nosum.Domain.Accounts;

namespace Nosum.Infrastructure.Accounts;

/// <summary>Register, sign in, resume and language preference on top of <see cref="IUnitOfWorkFactory"/>.</summary>
public sealed class AccountService : IAccountService
{
    /// <summary>How long a session token stays valid.</summary>
    public static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromDays(30);

    private const int MaxTokenLength = 128;

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IPasswordHasher _hasher;
    private readonly ISessionTokenService _tokens;
    private readonly TimeProvider _time;
    private readonly TimeSpan _sessionLifetime;
    private readonly Lazy<string> _decoyHash;

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="hasher">Hashes passwords.</param>
    /// <param name="tokens">Creates and hashes session tokens.</param>
    /// <param name="time">The clock; tests pass a fake one.</param>
    /// <param name="sessionLifetime">Session token lifetime; defaults to <see cref="DefaultSessionLifetime"/>.</param>
    public AccountService(
        IUnitOfWorkFactory unitOfWork,
        IPasswordHasher hasher,
        ISessionTokenService tokens,
        TimeProvider time,
        TimeSpan? sessionLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(time);
        _unitOfWork = unitOfWork;
        _hasher = hasher;
        _tokens = tokens;
        _time = time;
        _sessionLifetime = sessionLifetime ?? DefaultSessionLifetime;
        _decoyHash = new Lazy<string>(() => hasher.Hash(Guid.NewGuid().ToString("N")));
    }

    /// <inheritdoc/>
    public async Task<AccountOutcome> RegisterAsync(string username, string password, string language, CancellationToken cancellationToken)
    {
        if (!AccountRules.IsValidUsername(username))
        {
            return AccountOutcome.Refused(AccountStatus.InvalidUsername);
        }

        if (!AccountRules.IsValidPassword(password))
        {
            return AccountOutcome.Refused(AccountStatus.InvalidPassword);
        }

        string preferredLanguage = string.IsNullOrEmpty(language) ? AccountLimits.DefaultLanguage : language;
        if (!AccountRules.IsWellFormedLanguageTag(preferredLanguage))
        {
            return AccountOutcome.Refused(AccountStatus.InvalidLanguage);
        }

        string normalized = AccountRules.NormalizeUsername(username);
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<Player> players = unit.Repository<Player>();
            if (await players.AnyAsync(p => p.NormalizedUsername == normalized, cancellationToken).ConfigureAwait(false))
            {
                return AccountOutcome.Refused(AccountStatus.UsernameTaken);
            }

            DateTimeOffset now = _time.GetUtcNow();
            string hash = await Task.Run(() => _hasher.Hash(password), cancellationToken).ConfigureAwait(false);
            Player player = new()
            {
                Username = username,
                NormalizedUsername = normalized,
                PasswordHash = hash,
                PreferredLanguage = preferredLanguage,
                CreatedAt = now,
                LastLoginAt = now,
            };
            players.Add(player);
            IssuedToken token = this.AddToken(unit, player.Id, now, out DateTimeOffset expiresAt);

            try
            {
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (unit.IsUniqueViolation(ex))
            {
                return AccountOutcome.Refused(AccountStatus.UsernameTaken);
            }

            return new AccountOutcome(AccountStatus.Ok, player.Id, player.Username, token.Token, expiresAt, player.PreferredLanguage);
        }
    }

    /// <inheritdoc/>
    public async Task<AccountOutcome> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)
            || username.Length > AccountLimits.UsernameMaxLength || password.Length > AccountLimits.PasswordMaxLength)
        {
            return AccountOutcome.Refused(AccountStatus.InvalidCredentials);
        }

        string normalized = AccountRules.NormalizeUsername(username);
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>()
                .FirstOrDefaultAsync(p => p.NormalizedUsername == normalized, cancellationToken).ConfigureAwait(false);

            // An unknown username still pays for one hash check, so response time does not reveal which usernames exist.
            string storedHash = player?.PasswordHash ?? _decoyHash.Value;
            bool matches = await Task.Run(() => _hasher.Verify(password, storedHash), cancellationToken).ConfigureAwait(false);
            if (player is null || !matches)
            {
                return AccountOutcome.Refused(AccountStatus.InvalidCredentials);
            }

            DateTimeOffset now = _time.GetUtcNow();
            player.LastLoginAt = now;
            IssuedToken token = this.AddToken(unit, player.Id, now, out DateTimeOffset expiresAt);
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new AccountOutcome(AccountStatus.Ok, player.Id, player.Username, token.Token, expiresAt, player.PreferredLanguage);
        }
    }

    /// <inheritdoc/>
    public async Task<AccountOutcome> ResumeAsync(string sessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sessionToken) || sessionToken.Length > MaxTokenLength)
        {
            return AccountOutcome.Refused(AccountStatus.InvalidToken);
        }

        string hash = _tokens.Hash(sessionToken);
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            PlayerSessionToken? stored = await unit.Repository<PlayerSessionToken>()
                .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken).ConfigureAwait(false);
            DateTimeOffset now = _time.GetUtcNow();
            if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= now)
            {
                return AccountOutcome.Refused(AccountStatus.InvalidToken);
            }

            Player? player = await unit.Repository<Player>().FindAsync(stored.PlayerId, cancellationToken).ConfigureAwait(false);
            if (player is null)
            {
                return AccountOutcome.Refused(AccountStatus.InvalidToken);
            }

            player.LastLoginAt = now;
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new AccountOutcome(AccountStatus.Ok, player.Id, player.Username, sessionToken, stored.ExpiresAt, player.PreferredLanguage);
        }
    }

    /// <inheritdoc/>
    public async Task<AccountStatus> SetLanguageAsync(Guid playerId, string language, CancellationToken cancellationToken)
    {
        if (!AccountRules.IsWellFormedLanguageTag(language))
        {
            return AccountStatus.InvalidLanguage;
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (player is null)
            {
                return AccountStatus.InvalidToken;
            }

            player.PreferredLanguage = language;
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return AccountStatus.Ok;
        }
    }

    private IssuedToken AddToken(IUnitOfWork unit, Guid playerId, DateTimeOffset now, out DateTimeOffset expiresAt)
    {
        IssuedToken token = _tokens.Create();
        expiresAt = now + _sessionLifetime;
        unit.Repository<PlayerSessionToken>().Add(new PlayerSessionToken
        {
            PlayerId = playerId,
            TokenHash = token.Hash,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        return token;
    }
}
