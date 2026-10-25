// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Chat;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Friends;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Friends;
using Nosum.Contracts.Friends.S2C;
using Nosum.Domain.Accounts;
using Nosum.Domain.Social;
using Nosum.Domain.Visits;
using Nosum.Infrastructure.Chat;
using Nosum.Gameplay.Friends;
using Nosum.Gameplay.Visits;

namespace Nosum.Infrastructure.Friends;

/// <summary>
/// <see cref="IFriendService"/> (docs/adr/ADR-029 D4). A friendship is two <see cref="Friendship"/> rows written and removed in one save, so a
/// list never shows a half friendship. Online status is not stored: it is read from the session registry and the map presence, and pushed to
/// the mutual friends whenever it changes (a player joins a map, or has been off every map for a moment, which is how a map change is told
/// apart from leaving). Guests keep no friends list.
/// </summary>
public sealed class FriendService : IFriendService, IDisposable
{
    /// <summary>How long a player may be on no map before their friends are told they went offline; a map change takes far less.</summary>
    public static readonly TimeSpan OfflineGrace = TimeSpan.FromMilliseconds(750);

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IMapPresence _presence;
    private readonly ISessionRegistry _sessions;
    private readonly IPlayerNotifier _notifier;
    private readonly INicknameDirectory _nicknames;
    private readonly IBlockList _blocks;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, Location> _announced = new();

    /// <summary>Initializes the service and starts listening for players joining and leaving maps.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="presence">Where players are.</param>
    /// <param name="sessions">Who is connected.</param>
    /// <param name="notifier">Pushes snapshots and status changes.</param>
    /// <param name="nicknames">Resolves nicknames.</param>
    /// <param name="blocks">The block lists: nobody befriends a player who blocked them.</param>
    /// <param name="time">The clock for row times and the offline grace; tests pass a fake one.</param>
    public FriendService(IUnitOfWorkFactory unitOfWork, IMapPresence presence, ISessionRegistry sessions, IPlayerNotifier notifier, INicknameDirectory nicknames, IBlockList blocks, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(nicknames);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _presence = presence;
        _sessions = sessions;
        _notifier = notifier;
        _nicknames = nicknames;
        _blocks = blocks;
        _time = time;
        _presence.PlayerJoined += this.OnJoinedAsync;
        _presence.PlayerLeft += this.OnLeftAsync;
    }

    /// <inheritdoc/>
    public event Func<Guid, Guid?, ValueTask>? FriendVisitsRevoked;

    /// <inheritdoc/>
    public void Dispose()
    {
        _presence.PlayerJoined -= this.OnJoinedAsync;
        _presence.PlayerLeft -= this.OnLeftAsync;
    }

    /// <inheritdoc/>
    public async Task<FriendsView> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        bool isGuest;
        bool friendsMayVisit;
        List<Guid> friends;
        List<Guid> incoming;
        List<Guid> outgoing;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            PlayerFact? me = await _nicknames.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            isGuest = me?.IsGuest ?? true;
            VisitSettings? settings = await unit.Repository<VisitSettings>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            friendsMayVisit = settings?.FriendsMayVisit ?? VisitRules.FriendsMayVisitByDefault;
            friends = [.. (await unit.Repository<Friendship>().ListAsync(f => f.PlayerId == playerId, cancellationToken).ConfigureAwait(false)).OrderBy(f => f.CreatedAt).Select(f => f.FriendId)];
            incoming = [.. (await unit.Repository<FriendRequest>().ListAsync(r => r.ToId == playerId, cancellationToken).ConfigureAwait(false)).OrderBy(r => r.CreatedAt).Select(r => r.FromId)];
            outgoing = [.. (await unit.Repository<FriendRequest>().ListAsync(r => r.FromId == playerId, cancellationToken).ConfigureAwait(false)).OrderBy(r => r.CreatedAt).Select(r => r.ToId)];
        }

        IReadOnlyDictionary<Guid, string> names = await _nicknames.GetManyAsync(friends.Concat(incoming).Concat(outgoing), cancellationToken).ConfigureAwait(false);
        List<FriendView> friendViews = new(friends.Count);
        foreach (Guid id in friends)
        {
            Location where = this.Locate(id);
            friendViews.Add(new FriendView(id, names.GetValueOrDefault(id, string.Empty), where.Online, where.MapId));
        }

        return new FriendsView(
            friendsMayVisit,
            isGuest,
            friendViews,
            [.. incoming.Select(id => new FriendRequestView(id, names.GetValueOrDefault(id, string.Empty)))],
            [.. outgoing.Select(id => new FriendRequestView(id, names.GetValueOrDefault(id, string.Empty)))]);
    }

    /// <inheritdoc/>
    public async Task<FriendStatus> RequestAsync(Guid playerId, string nickname, CancellationToken cancellationToken = default)
    {
        if (!BlockList.TryKey(nickname, out string key))
        {
            return FriendStatus.InvalidNickname;
        }

        Guid targetId;
        bool nowFriends = false;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            PlayerFact? me = await _nicknames.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (me is null || me.IsGuest)
            {
                return FriendStatus.GuestNotAllowed;
            }

            PlayerFact? target = await _nicknames.FindByKeyAsync(key, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                return FriendStatus.UnknownPlayer;
            }

            targetId = target.Id;
            if (targetId == playerId)
            {
                return FriendStatus.Self;
            }

            if (target.IsGuest)
            {
                return FriendStatus.GuestNotAllowed;
            }

            // A block in either direction looks like an unknown player: the blocked one learns nothing.
            if (await _blocks.IsBlockedEitherWayAsync(playerId, targetId, cancellationToken).ConfigureAwait(false))
            {
                return FriendStatus.UnknownPlayer;
            }

            IRepository<Friendship> friendships = unit.Repository<Friendship>();
            IRepository<FriendRequest> requests = unit.Repository<FriendRequest>();
            if (await friendships.AnyAsync(f => f.PlayerId == playerId && f.FriendId == targetId, cancellationToken).ConfigureAwait(false))
            {
                return FriendStatus.AlreadyFriends;
            }

            if (await requests.AnyAsync(r => r.FromId == playerId && r.ToId == targetId, cancellationToken).ConfigureAwait(false))
            {
                return FriendStatus.AlreadyRequested;
            }

            if (await friendships.CountAsync(f => f.PlayerId == playerId, cancellationToken).ConfigureAwait(false) >= FriendRules.MaxFriends
                || await friendships.CountAsync(f => f.PlayerId == targetId, cancellationToken).ConfigureAwait(false) >= FriendRules.MaxFriends)
            {
                return FriendStatus.ListFull;
            }

            DateTimeOffset now = _time.GetUtcNow();
            FriendRequest? reverse = await requests.FirstOrDefaultAsync(r => r.FromId == targetId && r.ToId == playerId, cancellationToken).ConfigureAwait(false);
            if (reverse is not null)
            {
                // They already asked: asking back is accepting.
                requests.Remove(reverse);
                friendships.Add(new Friendship { PlayerId = playerId, FriendId = targetId, CreatedAt = now });
                friendships.Add(new Friendship { PlayerId = targetId, FriendId = playerId, CreatedAt = now });
                nowFriends = true;
            }
            else
            {
                if (await requests.CountAsync(r => r.FromId == playerId, cancellationToken).ConfigureAwait(false) >= FriendRules.MaxPending
                    || await requests.CountAsync(r => r.ToId == targetId, cancellationToken).ConfigureAwait(false) >= FriendRules.MaxPending)
                {
                    return FriendStatus.PendingFull;
                }

                requests.Add(new FriendRequest { FromId = playerId, ToId = targetId, CreatedAt = now });
            }

            try
            {
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (unit.IsUniqueViolation(ex) || unit.IsConcurrencyConflict(ex))
            {
                return nowFriends ? FriendStatus.AlreadyFriends : FriendStatus.AlreadyRequested;
            }
        }

        await this.PushSnapshotAsync(targetId, cancellationToken).ConfigureAwait(false);
        if (nowFriends)
        {
            await this.PushSnapshotAsync(playerId, cancellationToken).ConfigureAwait(false);
        }

        return FriendStatus.Ok;
    }

    /// <inheritdoc/>
    public async Task<FriendStatus> RespondAsync(Guid playerId, Guid fromId, bool accept, CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<FriendRequest> requests = unit.Repository<FriendRequest>();
            FriendRequest? request = await requests.FirstOrDefaultAsync(r => r.FromId == fromId && r.ToId == playerId, cancellationToken).ConfigureAwait(false);
            if (request is null)
            {
                return FriendStatus.NoRequest;
            }

            requests.Remove(request);
            if (accept)
            {
                IRepository<Friendship> friendships = unit.Repository<Friendship>();
                if (await friendships.CountAsync(f => f.PlayerId == playerId, cancellationToken).ConfigureAwait(false) >= FriendRules.MaxFriends
                    || await friendships.CountAsync(f => f.PlayerId == fromId, cancellationToken).ConfigureAwait(false) >= FriendRules.MaxFriends)
                {
                    return FriendStatus.ListFull;
                }

                if (!await friendships.AnyAsync(f => f.PlayerId == playerId && f.FriendId == fromId, cancellationToken).ConfigureAwait(false))
                {
                    DateTimeOffset now = _time.GetUtcNow();
                    friendships.Add(new Friendship { PlayerId = playerId, FriendId = fromId, CreatedAt = now });
                    friendships.Add(new Friendship { PlayerId = fromId, FriendId = playerId, CreatedAt = now });
                }
            }

            try
            {
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (unit.IsUniqueViolation(ex) || unit.IsConcurrencyConflict(ex))
            {
                return FriendStatus.Conflict;
            }
        }

        await this.PushSnapshotAsync(playerId, cancellationToken).ConfigureAwait(false);
        await this.PushSnapshotAsync(fromId, cancellationToken).ConfigureAwait(false);
        return FriendStatus.Ok;
    }

    /// <inheritdoc/>
    public async Task<FriendStatus> RemoveAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken = default)
    {
        bool wasFriends = false;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<Friendship> friendships = unit.Repository<Friendship>();
            List<Friendship> rows = await friendships.ListAsync(f => (f.PlayerId == playerId && f.FriendId == otherId) || (f.PlayerId == otherId && f.FriendId == playerId), cancellationToken).ConfigureAwait(false);
            wasFriends = rows.Count > 0;
            foreach (Friendship row in rows)
            {
                friendships.Remove(row);
            }

            IRepository<FriendRequest> requests = unit.Repository<FriendRequest>();
            List<FriendRequest> open = await requests.ListAsync(r => (r.FromId == playerId && r.ToId == otherId) || (r.FromId == otherId && r.ToId == playerId), cancellationToken).ConfigureAwait(false);
            foreach (FriendRequest row in open)
            {
                requests.Remove(row);
            }

            if (!wasFriends && open.Count == 0)
            {
                return FriendStatus.NotFriends;
            }

            try
            {
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (unit.IsConcurrencyConflict(ex))
            {
                return FriendStatus.Conflict;
            }
        }

        if (wasFriends)
        {
            // Each could have been a visitor of the other's farm through the friendship.
            await this.RaiseRevokedAsync(playerId, otherId).ConfigureAwait(false);
            await this.RaiseRevokedAsync(otherId, playerId).ConfigureAwait(false);
        }

        await this.PushSnapshotAsync(playerId, cancellationToken).ConfigureAwait(false);
        await this.PushSnapshotAsync(otherId, cancellationToken).ConfigureAwait(false);
        return FriendStatus.Ok;
    }

    /// <inheritdoc/>
    public async Task<FriendStatus> SetFriendsMayVisitAsync(Guid ownerId, bool allowed, CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            PlayerFact? owner = await _nicknames.FindAsync(ownerId, cancellationToken).ConfigureAwait(false);
            if (owner is null || owner.IsGuest)
            {
                return FriendStatus.GuestNotAllowed;
            }

            VisitSettings? settings = await unit.Repository<VisitSettings>().FindAsync(ownerId, cancellationToken).ConfigureAwait(false);
            if (settings is null)
            {
                settings = new VisitSettings { OwnerId = ownerId };
                unit.Repository<VisitSettings>().Add(settings);
            }

            settings.FriendsMayVisit = allowed;
            settings.UpdatedAt = _time.GetUtcNow();
            try
            {
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (unit.IsUniqueViolation(ex) || unit.IsConcurrencyConflict(ex))
            {
                return FriendStatus.Conflict;
            }
        }

        if (!allowed)
        {
            await this.RaiseRevokedAsync(ownerId, null).ConfigureAwait(false);
        }

        return FriendStatus.Ok;
    }

    /// <inheritdoc/>
    public async Task<bool> AreFriendsAsync(Guid a, Guid b, CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            return await unit.Repository<Friendship>().AnyAsync(f => f.PlayerId == a && f.FriendId == b, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RaiseRevokedAsync(Guid ownerId, Guid? visitorId)
    {
        if (this.FriendVisitsRevoked is not { } handlers)
        {
            return;
        }

        foreach (Func<Guid, Guid?, ValueTask> handler in handlers.GetInvocationList())
        {
            await handler(ownerId, visitorId).ConfigureAwait(false);
        }
    }

    // Where a player is now: online when connected to a map, and the public map they stand on ("" in a farm, a house or co-op land).
    private Location Locate(Guid playerId)
    {
        IConnection? connection = _sessions.GetActive(playerId);
        if (connection is null || connection.IsDisposed || !_presence.TryGet(connection, out PlayerPresence? player))
        {
            return Location.Offline;
        }

        return Of(player);
    }

    private static Location Of(PlayerPresence player) => new(true, player.Instance.IsPrivate ? string.Empty : player.MapId);

    [SuppressMessage("Design", "CA1031", Justification = "Raised from a map join; a failed push must never fail the join.")]
    private async ValueTask OnJoinedAsync(PlayerPresence player)
    {
        try
        {
            await this.AnnounceAsync(player.AccountId, Of(player)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Friends read the true status the next time they open the list.
        }
    }

    // A map change leaves and joins within moments: only a player who is still on no map after the grace period went offline.
    private ValueTask OnLeftAsync(PlayerPresence player)
    {
        Guid id = player.AccountId;
        _ = Task.Run(() => this.AnnounceAfterGraceAsync(id));
        return ValueTask.CompletedTask;
    }

    [SuppressMessage("Design", "CA1031", Justification = "A background check with no caller to report to; friends read the true status the next time they open the list.")]
    private async Task AnnounceAfterGraceAsync(Guid playerId)
    {
        try
        {
            await Task.Delay(OfflineGrace, _time).ConfigureAwait(false);
            await this.AnnounceAsync(playerId, this.Locate(playerId)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // See above.
        }
    }

    // Pushes the new location to the player's online friends when it differs from the one last announced.
    private async Task AnnounceAsync(Guid playerId, Location now)
    {
        Location before = _announced.GetValueOrDefault(playerId, Location.Offline);
        if (before == now)
        {
            return;
        }

        if (now.Online)
        {
            _announced[playerId] = now;
        }
        else
        {
            _ = _announced.TryRemove(playerId, out _);
        }

        List<Guid> friends;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            friends = [.. (await unit.Repository<Friendship>().ListAsync(f => f.PlayerId == playerId, CancellationToken.None).ConfigureAwait(false)).Select(f => f.FriendId)];
        }

        foreach (Guid friend in friends)
        {
            using PacketScope<FriendStatusChanged> lease = PacketFactory<FriendStatusChanged>.Acquire();
            lease.Value.PlayerId = playerId;
            lease.Value.Online = now.Online;
            lease.Value.MapId = now.MapId;
            _ = await _notifier.TrySendAsync(friend, lease.Value, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task PushSnapshotAsync(Guid playerId, CancellationToken cancellationToken)
    {
        if (_sessions.GetActive(playerId) is null)
        {
            return;
        }

        FriendsView view = await this.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        using PacketScope<FriendsSnapshot> lease = PacketFactory<FriendsSnapshot>.Acquire();
        view.FillSnapshot(lease.Value);
        _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct Location(bool Online, string MapId)
    {
        public static Location Offline => new(false, string.Empty);
    }
}
