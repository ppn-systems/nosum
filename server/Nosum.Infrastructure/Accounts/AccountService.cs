// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Identity;
using Nosum.Abstractions.Security;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Contracts.Validation;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Players;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Accounts;

/// <summary>
/// Guest, Google or Microsoft sign-in, identity link, resume, logout, language and nickname on top of <see cref="IUnitOfWorkFactory"/>
/// (docs/adr/ADR-069). An identity is verified before the database is touched, and the account is found by its provider and subject only.
/// </summary>
public sealed class AccountService : IAccountService
{
    /// <summary>How long a session token stays valid.</summary>
    public static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromDays(30);

    /// <summary>Most live session tokens one account keeps; issuing one more revokes the oldest (docs/adr/ADR-032 D5).</summary>
    public const int MaxSessionsPerAccount = 10;

    /// <summary>How long a token that was just replaced by a resume still works, so a lost reply can be retried.</summary>
    public static readonly TimeSpan RotationGrace = TimeSpan.FromMinutes(2);

    private const int MaxGuestNameAttempts = 12;

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IReadOnlyDictionary<ExternalProvider, IExternalTokenVerifier> _verifiers;
    private readonly ISessionTokenService _tokens;
    private readonly TimeProvider _time;
    private readonly TimeSpan _sessionLifetime;
    private readonly INicknameDirectory? _nicknames;
    private readonly StripedAsyncLock _renameLocks = new(64);

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="verifiers">The verifiers of the providers this server accepts; at most one per provider.</param>
    /// <param name="tokens">Creates and hashes session tokens.</param>
    /// <param name="time">The clock; tests pass a fake one.</param>
    /// <param name="sessionLifetime">Session token lifetime; defaults to <see cref="DefaultSessionLifetime"/>.</param>
    /// <param name="nicknames">The nickname cache to keep current, or null when nothing caches nicknames.</param>
    public AccountService(
        IUnitOfWorkFactory unitOfWork,
        IEnumerable<IExternalTokenVerifier> verifiers,
        ISessionTokenService tokens,
        TimeProvider time,
        TimeSpan? sessionLifetime = null,
        INicknameDirectory? nicknames = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(verifiers);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(time);
        _unitOfWork = unitOfWork;
        _verifiers = verifiers.ToDictionary(v => v.Provider);
        _tokens = tokens;
        _time = time;
        _sessionLifetime = sessionLifetime ?? DefaultSessionLifetime;
        _nicknames = nicknames;
    }

    /// <inheritdoc/>
    public async Task<AccountOutcome> SignInWithExternalAsync(ExternalProvider provider, string idToken, GameLanguage language, CancellationToken cancellationToken)
    {
        if (!GameLanguages.IsDefined(language))
        {
            return AccountOutcome.Refused(AccountStatus.InvalidLanguage);
        }

        ExternalIdentity? identity = await this.VerifyAsync(provider, idToken, cancellationToken).ConfigureAwait(false);
        if (identity is null)
        {
            return AccountOutcome.Refused(AccountStatus.InvalidIdToken);
        }

        AccountOutcome? signedIn = await this.SignInLinkedAsync(identity.Value, cancellationToken).ConfigureAwait(false);
        if (signedIn is not null)
        {
            return signedIn.Value;
        }

        AccountOutcome created = await this.CreateNeutralPlayerAsync(false, GameLanguages.Resolve(language), identity, cancellationToken).ConfigureAwait(false);
        if (created.Status != AccountStatus.IdentityTaken)
        {
            return created;
        }

        // The same identity was created by a concurrent request; that account is the one to sign in.
        return await this.SignInLinkedAsync(identity.Value, cancellationToken).ConfigureAwait(false) ?? AccountOutcome.Refused(AccountStatus.IdentityTaken);
    }

    /// <inheritdoc/>
    public Task<AccountOutcome> StartGuestAsync(GameLanguage language, CancellationToken cancellationToken)
    {
        if (!GameLanguages.IsDefined(language))
        {
            return Task.FromResult(AccountOutcome.Refused(AccountStatus.InvalidLanguage));
        }

        return this.CreateNeutralPlayerAsync(true, GameLanguages.Resolve(language), null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<AccountOutcome> LinkExternalIdentityAsync(Guid playerId, Guid sessionId, ExternalProvider provider, string idToken, string nickname, CancellationToken cancellationToken)
    {
        AccountStatus? nicknameRefusal = ToStatus(NicknameRules.Validate(nickname));
        if (nicknameRefusal is not null)
        {
            return AccountOutcome.Refused(nicknameRefusal.Value);
        }

        ExternalIdentity? identity = await this.VerifyAsync(provider, idToken, cancellationToken).ConfigureAwait(false);
        if (identity is null)
        {
            return AccountOutcome.Refused(AccountStatus.InvalidIdToken);
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (player is null || !player.IsGuest)
            {
                return AccountOutcome.Refused(AccountStatus.NotGuest);
            }

            ExternalIdentity linked = identity.Value;
            if (await unit.Repository<ExternalLogin>().AnyAsync(l => l.Provider == linked.Provider && l.Subject == linked.Subject, cancellationToken).ConfigureAwait(false))
            {
                return AccountOutcome.Refused(AccountStatus.IdentityTaken);
            }

            string chosen = NicknameRules.Normalize(nickname);
            string nicknameKey = NicknameRules.Key(chosen);
            if (await unit.Repository<Player>().AnyAsync(p => p.NormalizedNickname == nicknameKey && p.Id != playerId, cancellationToken).ConfigureAwait(false))
            {
                return AccountOutcome.Refused(AccountStatus.NicknameTaken);
            }

            DateTimeOffset now = _time.GetUtcNow();
            player.Email = linked.Email;
            player.IsGuest = false;
            player.Nickname = chosen;
            player.NormalizedNickname = nicknameKey;
            player.NicknameChangedAt = null;
            player.LastLoginAt = now;
            player.LastActiveAt = now;
            unit.Repository<ExternalLogin>().Add(NewLink(playerId, linked, now));

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
    public async Task<AccountOutcome> ResumeAsync(string sessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sessionToken) || sessionToken.Length > WireLimits.Token)
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

                // A guest keeps the neutral name the server gave them; choosing one is part of linking an identity.
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

    private static AccountStatus? ToStatus(NicknameVerdict verdict) => verdict switch
    {
        NicknameVerdict.Ok => null,
        NicknameVerdict.Reserved => AccountStatus.NicknameReserved,
        NicknameVerdict.Profane => AccountStatus.NicknameProfane,
        _ => AccountStatus.InvalidNickname,
    };

    private static ExternalLogin NewLink(Guid playerId, ExternalIdentity identity, DateTimeOffset now) => new()
    {
        PlayerId = playerId,
        Provider = identity.Provider,
        Subject = identity.Subject,
        LinkedAt = now,
    };

    // Verifies a token with the verifier of its provider. A provider without a verifier refuses every token.
    private async Task<ExternalIdentity?> VerifyAsync(ExternalProvider provider, string idToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(idToken) || idToken.Length > WireLimits.IdToken || !_verifiers.TryGetValue(provider, out IExternalTokenVerifier? verifier))
        {
            return null;
        }

        return await verifier.VerifyAsync(idToken, cancellationToken).ConfigureAwait(false);
    }

    // Signs in the account linked to an identity. Null when no account is linked to it.
    private async Task<AccountOutcome?> SignInLinkedAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            ExternalLogin? link = await unit.Repository<ExternalLogin>()
                .FirstOrDefaultAsync(l => l.Provider == identity.Provider && l.Subject == identity.Subject, cancellationToken).ConfigureAwait(false);
            if (link is null)
            {
                return null;
            }

            Player? player = await unit.Repository<Player>().FindAsync(link.PlayerId, cancellationToken).ConfigureAwait(false);
            if (player is null)
            {
                return AccountOutcome.Refused(AccountStatus.InvalidIdToken);
            }

            if (player.BannedAt is not null)
            {
                return AccountOutcome.Refused(AccountStatus.Banned);
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

    // Creates a player with a neutral name (guest-1234) and a session; a name clash draws again, with a wider range after a few tries.
    // A registered player created by sign-in also gets the neutral name and renames it once for free (docs/adr/ADR-069 D5).
    private async Task<AccountOutcome> CreateNeutralPlayerAsync(bool isGuest, GameLanguage language, ExternalIdentity? identity, CancellationToken cancellationToken)
    {
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
                    IsGuest = isGuest,
                    Email = identity?.Email,
                    Nickname = name,
                    NormalizedNickname = name,
                    Language = language,
                    CreatedAt = now,
                    LastLoginAt = now,
                    LastActiveAt = now,
                };
                unit.Repository<Player>().Add(player);
                if (identity is { } linked)
                {
                    unit.Repository<ExternalLogin>().Add(NewLink(player.Id, linked, now));
                }

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

    // Saves, turning a lost unique-index race into a normal refusal: a fresh read tells whether the nickname or the identity is the one taken.
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
                return AccountOutcome.Refused(nicknameTaken ? AccountStatus.NicknameTaken : AccountStatus.IdentityTaken);
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

    private static AccountOutcome ToOutcome(Player player, string token, Guid sessionId, DateTimeOffset expiresAt)
        => new(
            AccountStatus.Ok,
            player.Id,
            player.Email ?? string.Empty,
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
