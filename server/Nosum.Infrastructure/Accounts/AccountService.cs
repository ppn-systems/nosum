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

/// <summary>Guest, register, sign in, resume, upgrade, logout and language preference on top of <see cref="IUnitOfWorkFactory"/>.</summary>
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
    public async Task<AccountOutcome> RegisterAsync(string email, string password, string language, CancellationToken cancellationToken)
    {
        AccountStatus? refusal = ValidateRegistration(email, password);
        if (refusal is not null)
        {
            return AccountOutcome.Refused(refusal.Value);
        }

        string preferredLanguage = string.IsNullOrEmpty(language) ? AccountLimits.DefaultLanguage : language;
        if (!AccountRules.IsWellFormedLanguageTag(preferredLanguage))
        {
            return AccountOutcome.Refused(AccountStatus.InvalidLanguage);
        }

        string normalized = AccountRules.NormalizeEmail(email);
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<Player> players = unit.Repository<Player>();
            if (await players.AnyAsync(p => p.NormalizedEmail == normalized, cancellationToken).ConfigureAwait(false))
            {
                return AccountOutcome.Refused(AccountStatus.EmailTaken);
            }

            DateTimeOffset now = _time.GetUtcNow();
            string hash = await Task.Run(() => _hasher.Hash(password), cancellationToken).ConfigureAwait(false);
            Player player = new()
            {
                Email = email,
                NormalizedEmail = normalized,
                PasswordHash = hash,
                PreferredLanguage = preferredLanguage,
                CreatedAt = now,
                LastLoginAt = now,
                LastActiveAt = now,
            };
            players.Add(player);
            AccountOutcome outcome = this.AddSession(unit, player, now);

            return await SaveAsync(unit, outcome, AccountStatus.EmailTaken, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<AccountOutcome> StartGuestAsync(string language, CancellationToken cancellationToken)
    {
        string preferredLanguage = string.IsNullOrEmpty(language) ? AccountLimits.DefaultLanguage : language;
        if (!AccountRules.IsWellFormedLanguageTag(preferredLanguage))
        {
            return AccountOutcome.Refused(AccountStatus.InvalidLanguage);
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            DateTimeOffset now = _time.GetUtcNow();
            Player player = new()
            {
                IsGuest = true,
                PreferredLanguage = preferredLanguage,
                CreatedAt = now,
                LastLoginAt = now,
                LastActiveAt = now,
            };
            unit.Repository<Player>().Add(player);
            AccountOutcome outcome = this.AddSession(unit, player, now);

            return await SaveAsync(unit, outcome, AccountStatus.InvalidToken, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<AccountOutcome> UpgradeGuestAsync(Guid playerId, Guid sessionId, string email, string password, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (player is null || !player.IsGuest)
            {
                return AccountOutcome.Refused(AccountStatus.NotGuest);
            }

            AccountStatus? refusal = ValidateRegistration(email, password);
            if (refusal is not null)
            {
                return AccountOutcome.Refused(refusal.Value);
            }

            string normalized = AccountRules.NormalizeEmail(email);
            if (await unit.Repository<Player>().AnyAsync(p => p.NormalizedEmail == normalized, cancellationToken).ConfigureAwait(false))
            {
                return AccountOutcome.Refused(AccountStatus.EmailTaken);
            }

            DateTimeOffset now = _time.GetUtcNow();
            string hash = await Task.Run(() => _hasher.Hash(password), cancellationToken).ConfigureAwait(false);
            player.Email = email;
            player.NormalizedEmail = normalized;
            player.PasswordHash = hash;
            player.IsGuest = false;
            player.LastLoginAt = now;
            player.LastActiveAt = now;

            // A change of privilege gets a fresh token; the guest token stops working.
            PlayerSessionToken? old = await unit.Repository<PlayerSessionToken>().FindAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (old is not null && old.PlayerId == playerId)
            {
                old.RevokedAt = now;
            }

            AccountOutcome outcome = this.AddSession(unit, player, now);
            return await SaveAsync(unit, outcome, AccountStatus.EmailTaken, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<AccountOutcome> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)
            || email.Length > AccountLimits.EmailMaxLength || password.Length > AccountLimits.PasswordMaxLength)
        {
            return AccountOutcome.Refused(AccountStatus.InvalidCredentials);
        }

        string normalized = AccountRules.NormalizeEmail(email);
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>()
                .FirstOrDefaultAsync(p => p.NormalizedEmail == normalized, cancellationToken).ConfigureAwait(false);

            // An unknown e-mail still pays for one hash check, so response time does not reveal which addresses exist.
            string storedHash = player?.PasswordHash ?? _decoyHash.Value;
            bool matches = await Task.Run(() => _hasher.Verify(password, storedHash), cancellationToken).ConfigureAwait(false);
            if (player is null || !matches)
            {
                return AccountOutcome.Refused(AccountStatus.InvalidCredentials);
            }

            DateTimeOffset now = _time.GetUtcNow();
            player.LastLoginAt = now;
            player.LastActiveAt = now;
            AccountOutcome outcome = this.AddSession(unit, player, now);
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return outcome;
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
            player.LastActiveAt = now;
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return ToOutcome(player, sessionToken, stored.Id, stored.ExpiresAt);
        }
    }

    /// <inheritdoc/>
    public async Task LogoutAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            PlayerSessionToken? stored = await unit.Repository<PlayerSessionToken>().FindAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (stored is not null && stored.RevokedAt is null)
            {
                stored.RevokedAt = _time.GetUtcNow();
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
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

    private static AccountStatus? ValidateRegistration(string email, string password)
    {
        if (!AccountRules.IsValidEmail(email))
        {
            return AccountStatus.InvalidEmail;
        }

        return AccountRules.IsValidPassword(password) ? null : AccountStatus.InvalidPassword;
    }

    // Saves, turning a lost unique-index race (two requests for the same e-mail) into a normal refusal.
    private static async Task<AccountOutcome> SaveAsync(IUnitOfWork unit, AccountOutcome outcome, AccountStatus onUniqueViolation, CancellationToken cancellationToken)
    {
        try
        {
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return outcome;
        }
        catch (DbUpdateException ex) when (unit.IsUniqueViolation(ex))
        {
            return AccountOutcome.Refused(onUniqueViolation);
        }
    }

    private static AccountOutcome ToOutcome(Player player, string token, Guid sessionId, DateTimeOffset expiresAt)
        => new(
            AccountStatus.Ok,
            player.Id,
            player.NormalizedEmail ?? string.Empty,
            token,
            sessionId,
            expiresAt,
            player.PreferredLanguage,
            player.IsGuest,
            player.RegistrationPromptUnlockedAt is not null);

    private AccountOutcome AddSession(IUnitOfWork unit, Player player, DateTimeOffset now)
    {
        IssuedToken token = _tokens.Create();
        DateTimeOffset expiresAt = now + _sessionLifetime;
        PlayerSessionToken stored = new()
        {
            PlayerId = player.Id,
            TokenHash = token.Hash,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };
        unit.Repository<PlayerSessionToken>().Add(stored);
        return ToOutcome(player, token.Token, stored.Id, expiresAt);
    }
}
