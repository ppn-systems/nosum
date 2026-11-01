// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Friends;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Contracts.Friends;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Players;

namespace Nosum.Infrastructure.Dev;

/// <summary>
/// <see cref="IDevSocialService"/> (docs/adr/ADR-105). The friendship is the real one: the test player asks the sender, and the sender asks back, which
/// the friend service treats as accepting. Registered by the host only in Development.
/// </summary>
public sealed class DevSocialService : IDevSocialService
{
    private const int MaxNameAttempts = 12;

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IAccountService _accounts;
    private readonly IFriendService _friends;
    private readonly INicknameDirectory _nicknames;

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="accounts">Starts the test player's account and session.</param>
    /// <param name="friends">The friend service, which makes the friendship.</param>
    /// <param name="nicknames">The nickname directory, kept current after a change of privilege or name.</param>
    public DevSocialService(IUnitOfWorkFactory unitOfWork, IAccountService accounts, IFriendService friends, INicknameDirectory nicknames)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(friends);
        ArgumentNullException.ThrowIfNull(nicknames);
        _unitOfWork = unitOfWork;
        _accounts = accounts;
        _friends = friends;
        _nicknames = nicknames;
    }

    /// <inheritdoc/>
    public async Task<DevFriend?> BefriendNewTestPlayerAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        PlayerFact? me = await _nicknames.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
        if (me is null)
        {
            return null;
        }

        // A friends list needs a registered account on both sides (docs/adr/ADR-029 D4); this makes the tester's own account one.
        if (me.IsGuest && !await this.PromoteSenderAsync(playerId, me.Nickname, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        GameLanguage? language = await this.LanguageOfAsync(playerId, cancellationToken).ConfigureAwait(false);
        if (language is null)
        {
            return null;
        }

        AccountOutcome created = await _accounts.StartGuestAsync(language.Value, cancellationToken).ConfigureAwait(false);
        if (created.Status != AccountStatus.Ok)
        {
            return null;
        }

        string? name = await this.GiveRegisteredNameAsync(created.PlayerId, cancellationToken).ConfigureAwait(false);
        if (name is null)
        {
            return null;
        }

        FriendStatus asked = await _friends.RequestAsync(created.PlayerId, me.Nickname, cancellationToken).ConfigureAwait(false);
        if (asked != FriendStatus.Ok)
        {
            return null;
        }

        // The sender asks back: that is accepting the request just sent.
        FriendStatus accepted = await _friends.RequestAsync(playerId, name, cancellationToken).ConfigureAwait(false);
        return accepted == FriendStatus.Ok ? new DevFriend(name, created.SessionToken) : null;
    }

    private async Task<GameLanguage?> LanguageOfAsync(Guid playerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            return player?.Language;
        }
    }

    // Makes a guest a registered account, keeping its nickname.
    private async Task<bool> PromoteSenderAsync(Guid playerId, string nickname, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (player is null)
            {
                return false;
            }

            player.IsGuest = false;
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _nicknames.Remember(playerId, nickname);
        return true;
    }

    // Makes the new test player a registered account with a name no other player has, and returns the name.
    private async Task<string?> GiveRegisteredNameAsync(Guid playerId, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaxNameAttempts; attempt++)
        {
            string name = "QaFriend" + RandomNumberGenerator.GetInt32(1000, 10_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string key = NicknameRules.Key(name);
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                if (await unit.Repository<Player>().AnyAsync(p => p.NormalizedNickname == key, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                Player? player = await unit.Repository<Player>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
                if (player is null)
                {
                    return null;
                }

                player.IsGuest = false;
                player.Nickname = name;
                player.NormalizedNickname = key;
                player.NicknameChangedAt = null;
                try
                {
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (DbUpdateException ex) when (unit.IsUniqueViolation(ex))
                {
                    continue;
                }
            }

            _nicknames.Remember(playerId, name);
            return name;
        }

        return null;
    }
}
