// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Chat;
using Nosum.Abstractions.Data;
using Nosum.Contracts.Chat;
using Nosum.Domain.Accounts;
using Nosum.Domain.Social;
using Nosum.Gameplay.Chat;
using Nosum.Gameplay.Players;

namespace Nosum.Infrastructure.Chat;

/// <summary>
/// <see cref="IBlockList"/> over the <c>blocked_players</c> table with an in-memory copy per owner. The server is one process
/// (docs/adr/ADR-009), so the copy is exact: every change goes through this class, which drops the owner's copy and reloads it on the
/// next question. Blocking a player also ends the friendship and any open friend request between the two (docs/adr/ADR-029 D3-D4).
/// </summary>
public sealed class BlockList : IBlockList
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly INicknameDirectory _nicknames;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, HashSet<Guid>> _cache = new();
    private long _epoch;

    /// <summary>Initializes the block list.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="nicknames">Resolves the nicknames shown in the list.</param>
    /// <param name="time">The clock for the time a block was made.</param>
    public BlockList(IUnitOfWorkFactory unitOfWork, INicknameDirectory nicknames, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(nicknames);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _nicknames = nicknames;
        _time = time;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> HasBlockedAsync(Guid ownerId, Guid otherId, CancellationToken cancellationToken = default)
    {
        HashSet<Guid> blocked = await this.LoadAsync(ownerId, cancellationToken).ConfigureAwait(false);
        return blocked.Contains(otherId);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> IsBlockedEitherWayAsync(Guid a, Guid b, CancellationToken cancellationToken = default)
        => await this.HasBlockedAsync(a, b, cancellationToken).ConfigureAwait(false) || await this.HasBlockedAsync(b, a, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BlockedView>> ListAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        List<BlockedPlayer> rows;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            rows = await unit.Repository<BlockedPlayer>().ListAsync(b => b.OwnerId == ownerId, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyDictionary<Guid, string> names = await _nicknames.GetManyAsync(rows.Select(r => r.BlockedId), cancellationToken).ConfigureAwait(false);
        return [.. rows.OrderBy(r => r.CreatedAt).Select(r => new BlockedView(r.BlockedId, names.GetValueOrDefault(r.BlockedId, string.Empty)))];
    }

    /// <inheritdoc/>
    public async Task<ChatStatus> BlockAsync(Guid ownerId, string nickname, CancellationToken cancellationToken = default)
    {
        if (!TryKey(nickname, out string key))
        {
            return ChatStatus.InvalidNickname;
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? target = await unit.Repository<Player>().FirstOrDefaultAsync(p => p.NormalizedNickname == key, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                return ChatStatus.UnknownPlayer;
            }

            Guid targetId = target.Id;
            if (targetId == ownerId)
            {
                return ChatStatus.SelfBlock;
            }

            IRepository<BlockedPlayer> blocks = unit.Repository<BlockedPlayer>();
            if (await blocks.AnyAsync(b => b.OwnerId == ownerId && b.BlockedId == targetId, cancellationToken).ConfigureAwait(false))
            {
                return ChatStatus.AlreadyBlocked;
            }

            if (await blocks.CountAsync(b => b.OwnerId == ownerId, cancellationToken).ConfigureAwait(false) >= ChatRules.MaxBlocked)
            {
                return ChatStatus.BlockListFull;
            }

            blocks.Add(new BlockedPlayer { OwnerId = ownerId, BlockedId = targetId, CreatedAt = _time.GetUtcNow() });

            // A blocked player is no friend: both sides of the friendship and any open request go in the same save.
            IRepository<Friendship> friendships = unit.Repository<Friendship>();
            foreach (Friendship row in await friendships.ListAsync(f => (f.PlayerId == ownerId && f.FriendId == targetId) || (f.PlayerId == targetId && f.FriendId == ownerId), cancellationToken).ConfigureAwait(false))
            {
                friendships.Remove(row);
            }

            IRepository<FriendRequest> requests = unit.Repository<FriendRequest>();
            foreach (FriendRequest row in await requests.ListAsync(r => (r.FromId == ownerId && r.ToId == targetId) || (r.FromId == targetId && r.ToId == ownerId), cancellationToken).ConfigureAwait(false))
            {
                requests.Remove(row);
            }

            try
            {
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (unit.IsUniqueViolation(ex))
            {
                return ChatStatus.AlreadyBlocked;
            }
        }

        _ = Interlocked.Increment(ref _epoch);
        _ = _cache.TryRemove(ownerId, out _);
        return ChatStatus.Ok;
    }

    /// <inheritdoc/>
    public async Task<ChatStatus> UnblockAsync(Guid ownerId, string nickname, CancellationToken cancellationToken = default)
    {
        if (!TryKey(nickname, out string key))
        {
            return ChatStatus.InvalidNickname;
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? target = await unit.Repository<Player>().FirstOrDefaultAsync(p => p.NormalizedNickname == key, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                return ChatStatus.UnknownPlayer;
            }

            Guid targetId = target.Id;
            IRepository<BlockedPlayer> blocks = unit.Repository<BlockedPlayer>();
            BlockedPlayer? row = await blocks.FirstOrDefaultAsync(b => b.OwnerId == ownerId && b.BlockedId == targetId, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                return ChatStatus.NotBlocked;
            }

            blocks.Remove(row);
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _ = Interlocked.Increment(ref _epoch);
        _ = _cache.TryRemove(ownerId, out _);
        return ChatStatus.Ok;
    }

    private async ValueTask<HashSet<Guid>> LoadAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(ownerId, out HashSet<Guid>? cached))
        {
            return cached;
        }

        // A change made while this read was in flight bumps the epoch: the (possibly stale) result is used once and not kept.
        long epoch = Volatile.Read(ref _epoch);
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<BlockedPlayer> rows = await unit.Repository<BlockedPlayer>().ListAsync(b => b.OwnerId == ownerId, cancellationToken).ConfigureAwait(false);
            HashSet<Guid> set = [.. rows.Select(r => r.BlockedId)];
            if (Volatile.Read(ref _epoch) == epoch)
            {
                _cache[ownerId] = set;
            }

            return set;
        }
    }

    internal static bool TryKey(string? typed, out string key)
    {
        string name = NicknameRules.Normalize(typed);
        if (NicknameRules.Validate(name) == NicknameVerdict.Ok || NicknameRules.IsGuestName(name, out _))
        {
            key = NicknameRules.Key(name);
            return true;
        }

        key = string.Empty;
        return false;
    }
}
