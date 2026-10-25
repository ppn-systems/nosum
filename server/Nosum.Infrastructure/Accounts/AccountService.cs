// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Security;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Players;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Accounts;

/// <summary>Guest, register, sign in, resume, upgrade, logout and language preference on top of <see cref="IUnitOfWorkFactory"/>.</summary>
public sealed class AccountService : IAccountService
{
    /// <summary>How long a session token stays valid.</summary>
    public static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromDays(30);

    private const int MaxTokenLength = 128;
    private const int MaxGuestNameAttempts = 12;

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IPasswordHasher _hasher;
    private readonly ISessionTokenService _tokens;
    private readonly TimeProvider _time;
    private readonly TimeSpan _sessionLifetime;
    private readonly Lazy<string> _decoyHash;
    private readonly INicknameDirectory? _nicknames;
    private readonly StripedAsyncLock _renameLocks = new(64);

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="hasher">Hashes passwords.</param>
    /// <param name="tokens">Creates and hashes session tokens.</param>
    /// <param name="time">The clock; tests pass a fake one.</param>
    /// <param name="sessionLifetime">Session token lifetime; defaults to <see cref="DefaultSessionLifetime"/>.</param>
    /// <param name="nicknames">The nickname cache to keep current, or null when nothing caches nicknames.</param>
    public AccountService(
        IUnitOfWorkFactory unitOfWork,
        IPasswordHasher hasher,
        ISessionTokenService tokens,
        TimeProvider time,
        TimeSpan? sessionLifetime = null,
        INicknameDirectory? nicknames = null)
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
        _nicknames = nicknames;
        _decoyHash = new Lazy<string>(() => hasher.Hash(Guid.NewGuid().ToString("N")));
    }

    /// <inheritdoc/>
    public async Task<AccountOutcome> RegisterAsync(string email, string password, string nickname, GameLanguage language, CancellationToken cancellationToken)
    {
        AccountStatus? refusal = ValidateRegistration(email, password, nickname);
        if (refusal is not null)
        {
            return AccountOutcome.Refused(refusal.Value);
        }

        if (!GameLanguages.IsDefined(language))
        {
            return AccountOutcome.Refused(AccountStatus.InvalidLanguage);
        }

        GameLanguage preferredLanguage = GameLanguages.Resolve(language);

        string normalized = AccountRules.NormalizeEmail(email);
        string chosen = NicknameRules.Normalize(nickname);
        string nicknameKey = NicknameRules.Key(chosen);
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<Player> players = unit.Repository<Player>();
            if (await players.AnyAsync(p => p.NormalizedEmail == normalized, cancellationToken).ConfigureAwait(false))
            {
                return AccountOutcome.Refused(AccountStatus.EmailTaken);
            }

            if (await players.AnyAsync(p => p.NormalizedNickname == nicknameKey, cancellationToken).ConfigureAwait(false))
            {
                return AccountOutcome.Refused(AccountStatus.NicknameTaken);
            }

            DateTimeOffset now = _time.GetUtcNow();
            string hash = await Task.Run(() => _hasher.Hash(password), cancellationToken).ConfigureAwait(false);
            Player player = new()
            {
                Email = email,
                NormalizedEmail = normalized,
                PasswordHash = hash,
                Nickname = chosen,
                NormalizedNickname = nicknameKey,
                Language = preferredLanguage,
                CreatedAt = now,
                LastLoginAt = now,
                LastActiveAt = now,
            };
            players.Add(player);
            AccountOutcome outcome = this.AddSession(unit, player, now);

            return await this.SaveAsync(unit, outcome, nicknameKey, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<AccountOutcome> StartGuestAsync(GameLanguage language, CancellationToken cancellationToken)
    {
        if (!GameLanguages.IsDefined(language))
        {
            return AccountOutcome.Refused(AccountStatus.InvalidLanguage);
        }

        GameLanguage preferredLanguage = GameLanguages.Resolve(language);

        // A neutral name like guest-1234 is picked at random; a clash (checked, or lost to a concurrent guest) just draws again, with a wider range after a few tries.
        for (int attempt = 0; attempt < MaxGuestNameAttempts; attempt++)
        {
            string name = NicknameRules.GuestName(RandomNumberGenerator.GetInt32(0, attempt < 4 ? 10_000 : 100_000_000));
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                if (await unit.Repository<Player>().AnyAsync(p => p.NormalizedNickname == name, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                DateTimeOffset now = _time.GetUtcNow();
                Player player = new()
                {
                    IsGuest = true,
                    Nickname = name,
                    NormalizedNickname = name,
                    Language = preferredLanguage,
                    CreatedAt = now,
                    LastLoginAt = now,
                    LastActiveAt = now,
                };
                unit.Repository<Player>().Add(player);
                AccountOutcome outcome = this.AddSession(unit, player, now);

                AccountOutcome saved = await this.SaveAsync(unit, outcome, name, cancellationToken).ConfigureAwait(false);
                if (saved.Status != AccountStatus.NicknameTaken)
                {
                    return saved;
                }
            }
        }

        return AccountOutcome.Refused(AccountStatus.NicknameTaken);
    }

    /// <inheritdoc/>
    public async Task<AccountOutcome> UpgradeGuestAsync(Guid playerId, Guid sessionId, string email, string password, string nickname, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (player is null || !player.IsGuest)
            {
                return AccountOutcome.Refused(AccountStatus.NotGuest);
            }

            AccountStatus? refusal = ValidateRegistration(email, password, nickname);
            if (refusal is not null)
            {
                return AccountOutcome.Refused(refusal.Value);
            }

            string normalized = AccountRules.NormalizeEmail(email);
            string chosen = NicknameRules.Normalize(nickname);
            string nicknameKey = NicknameRules.Key(chosen);
            if (await unit.Repository<Player>().AnyAsync(p => p.NormalizedEmail == normalized, cancellationToken).ConfigureAwait(false))
            {
                return AccountOutcome.Refused(AccountStatus.EmailTaken);
            }

            if (await unit.Repository<Player>().AnyAsync(p => p.NormalizedNickname == nicknameKey && p.Id != playerId, cancellationToken).ConfigureAwait(false))
            {
                return AccountOutcome.Refused(AccountStatus.NicknameTaken);
            }

            DateTimeOffset now = _time.GetUtcNow();
            string hash = await Task.Run(() => _hasher.Hash(password), cancellationToken).ConfigureAwait(false);
            player.Email = email;
            player.NormalizedEmail = normalized;
            player.PasswordHash = hash;
            player.IsGuest = false;
            player.Nickname = chosen;
            player.NormalizedNickname = nicknameKey;
            player.NicknameChangedAt = null;
            player.LastLoginAt = now;
            player.LastActiveAt = now;

            // A change of privilege gets a fresh token; the guest token stops working.
            PlayerSessionToken? old = await unit.Repository<PlayerSessionToken>().FindAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (old is not null && old.PlayerId == playerId)
            {
                old.RevokedAt = now;
            }

            AccountOutcome outcome = this.AddSession(unit, player, now);
            return await this.SaveAsync(unit, outcome, nicknameKey, cancellationToken).ConfigureAwait(false);
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

            // Only someone who knows the password learns of the ban.
            if (player.BannedAt is not null)
            {
                return AccountOutcome.Refused(AccountStatus.Banned);
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

            if (player.BannedAt is not null)
            {
                return AccountOutcome.Refused(AccountStatus.Banned);
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
    public async Task<AccountStatus> SetLanguageAsync(Guid playerId, GameLanguage language, CancellationToken cancellationToken)
    {
        if (!GameLanguages.IsDefined(language))
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

            player.Language = GameLanguages.Resolve(language);
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return AccountStatus.Ok;
        }
    }

    /// <inheritdoc/>
    public async Task<NicknameOutcome> SetNicknameAsync(Guid playerId, string nickname, CancellationToken cancellationToken)
    {
        // One rename at a time per player, so two requests cannot both pass the 24-hour wait.
        using (await _renameLocks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                Player? player = await unit.Repository<Player>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
                if (player is null)
                {
                    return new NicknameOutcome(AccountStatus.InvalidToken, string.Empty, null);
                }

                // A guest keeps the neutral name the server gave them; choosing one is part of registering.
                if (player.IsGuest)
                {
                    return new NicknameOutcome(AccountStatus.NicknameRequiresAccount, player.Nickname, null);
                }

                AccountStatus? refusal = ToStatus(NicknameRules.Validate(nickname));
                if (refusal is not null)
                {
                    return new NicknameOutcome(refusal.Value, player.Nickname, NextChange(player));
                }

                string chosen = NicknameRules.Normalize(nickname);
                if (string.Equals(chosen, player.Nickname, StringComparison.Ordinal))
                {
                    return new NicknameOutcome(AccountStatus.Ok, player.Nickname, NextChange(player));
                }

                DateTimeOffset now = _time.GetUtcNow();
                if (NextChange(player) is { } next && next > now)
                {
                    return new NicknameOutcome(AccountStatus.NicknameTooSoon, player.Nickname, next);
                }

                string key = NicknameRules.Key(chosen);
                if (await unit.Repository<Player>().AnyAsync(p => p.NormalizedNickname == key && p.Id != playerId, cancellationToken).ConfigureAwait(false))
                {
                    return new NicknameOutcome(AccountStatus.NicknameTaken, player.Nickname, NextChange(player));
                }

                string previous = player.Nickname;
                player.Nickname = chosen;
                player.NormalizedNickname = key;
                player.NicknameChangedAt = now;
                try
                {
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (DbUpdateException ex) when (unit.IsUniqueViolation(ex))
                {
                    return new NicknameOutcome(AccountStatus.NicknameTaken, previous, null);
                }

                _nicknames?.Remember(playerId, chosen);
                return new NicknameOutcome(AccountStatus.Ok, chosen, NextChange(player));
            }
        }
    }

    private static DateTimeOffset? NextChange(Player player)
        => player.NicknameChangedAt?.AddHours(NicknameRules.RenameCooldownHours);

    private static AccountStatus? ValidateRegistration(string email, string password, string nickname)
    {
        if (!AccountRules.IsValidEmail(email))
        {
            return AccountStatus.InvalidEmail;
        }

        if (!AccountRules.IsValidPassword(password))
        {
            return AccountStatus.InvalidPassword;
        }

        return ToStatus(NicknameRules.Validate(nickname));
    }

    private static AccountStatus? ToStatus(NicknameVerdict verdict) => verdict switch
    {
        NicknameVerdict.Ok => null,
        NicknameVerdict.Reserved => AccountStatus.NicknameReserved,
        NicknameVerdict.Profane => AccountStatus.NicknameProfane,
        _ => AccountStatus.InvalidNickname,
    };

    // Saves, turning a lost unique-index race (two requests for the same e-mail or nickname) into a normal refusal:
    // a fresh read tells which of the two is now taken.
    private async Task<AccountOutcome> SaveAsync(IUnitOfWork unit, AccountOutcome outcome, string nicknameKey, CancellationToken cancellationToken)
    {
        try
        {
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _nicknames?.Remember(outcome.PlayerId, outcome.Nickname);
            return outcome;
        }
        catch (DbUpdateException ex) when (unit.IsUniqueViolation(ex))
        {
            IUnitOfWork check = _unitOfWork.Create();
            await using (check.ConfigureAwait(false))
            {
                bool nicknameTaken = await check.Repository<Player>().AnyAsync(p => p.NormalizedNickname == nicknameKey, cancellationToken).ConfigureAwait(false);
                return AccountOutcome.Refused(nicknameTaken ? AccountStatus.NicknameTaken : AccountStatus.EmailTaken);
            }
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
            player.Language,
            player.IsGuest,
            player.RegistrationPromptUnlockedAt is not null,
            player.Nickname,
            player.Role == PlayerRole.Admin);

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
