// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Email;
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

    /// <summary>Most live session tokens one account keeps; issuing one more revokes the oldest (docs/adr/ADR-032 D5).</summary>
    public const int MaxSessionsPerAccount = 10;

    /// <summary>How long a token that was just replaced by a resume still works, so a lost reply can be retried.</summary>
    public static readonly TimeSpan RotationGrace = TimeSpan.FromMinutes(2);

    /// <summary>How long a recovery code is accepted.</summary>
    public static readonly TimeSpan ResetCodeLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Wrong tries a recovery code survives.</summary>
    public const int ResetMaxAttempts = 5;

    /// <summary>Recovery codes one account may be sent per hour; more requests are ignored without telling the caller.</summary>
    public const int ResetRequestsPerHour = 3;

    private const int MaxTokenLength = 128;
    private const int MaxGuestNameAttempts = 12;
    private const int ResetCodeDigits = 8;

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IPasswordHasher _hasher;
    private readonly ISessionTokenService _tokens;
    private readonly TimeProvider _time;
    private readonly TimeSpan _sessionLifetime;
    private readonly Lazy<string> _decoyHash;
    private readonly INicknameDirectory? _nicknames;
    private readonly StripedAsyncLock _renameLocks = new(64);
    private readonly StripedAsyncLock _credentialLocks = new(64);
    private readonly IEmailSender? _email;

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="hasher">Hashes passwords.</param>
    /// <param name="tokens">Creates and hashes session tokens.</param>
    /// <param name="time">The clock; tests pass a fake one.</param>
    /// <param name="sessionLifetime">Session token lifetime; defaults to <see cref="DefaultSessionLifetime"/>.</param>
    /// <param name="nicknames">The nickname cache to keep current, or null when nothing caches nicknames.</param>
    /// <param name="email">Sends recovery codes; with null a recovery request is accepted but nothing is sent.</param>
    public AccountService(
        IUnitOfWorkFactory unitOfWork,
        IPasswordHasher hasher,
        ISessionTokenService tokens,
        TimeProvider time,
        TimeSpan? sessionLifetime = null,
        INicknameDirectory? nicknames = null,
        IEmailSender? email = null)
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
        _email = email;
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

            DateTimeOffset now = _time.GetUtcNow();
            player.LastLoginAt = now;
            player.LastActiveAt = now;
            await EnforceSessionCapAsync(unit, player.Id, now, keep: null, cancellationToken).ConfigureAwait(false);
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

            // Rotation (docs/adr/ADR-032 D5): every resume hands out a fresh token and a fresh idle window. The old one works for a short grace only,
            // so a reply lost on the way can be retried, but a copied token stops being useful soon after the real owner comes back.
            DateTimeOffset graceEnd = now + RotationGrace;
            if (stored.ExpiresAt > graceEnd)
            {
                stored.ExpiresAt = graceEnd;
            }

            await EnforceSessionCapAsync(unit, player.Id, now, stored.Id, cancellationToken).ConfigureAwait(false);
            AccountOutcome outcome = this.AddSession(unit, player, now);
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return outcome;
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
            player.Nickname);

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
