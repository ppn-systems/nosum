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
using Nosum.Infrastructure.Visits;
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
    private readonly FriendFacts _friends;
    private readonly VisitFacts _visits;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, Location> _announced = new();

    /// <summary>Initializes the service and starts listening for players joining and leaving maps.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="presence">Where players are.</param>
    /// <param name="sessions">Who is connected.</param>
    /// <param name="notifier">Pushes snapshots and status changes.</param>
    /// <param name="nicknames">Resolves nicknames.</param>
    /// <param name="blocks">The block lists: nobody befriends a player who blocked them.</param>
    /// <param name="time">The clock for row times and the offline grace; tests pass a fake one.</param>
    /// <param name="friends">Friends and requests per player, held in memory (docs/adr/ADR-035 D5); this service replaces what it changed. <see langword="null"/> reads the rows every time.</param>
    /// <param name="visits">The owners' visit settings, held in memory; this service drops what it changed. <see langword="null"/> reads the rows every time.</param>
    public FriendService(IUnitOfWorkFactory unitOfWork, IMapPresence presence, ISessionRegistry sessions, IPlayerNotifier notifier, INicknameDirectory nicknames, IBlockList blocks, TimeProvider time, FriendFacts? friends = null, VisitFacts? visits = null)
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
        _friends = friends ?? new FriendFacts(unitOfWork, cached: false);
        _visits = visits ?? new VisitFacts(unitOfWork, cached: false);
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
        _gate.Dispose();
    }

    /// <inheritdoc/>
    public async Task<FriendsView> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        PlayerFact? me = await _nicknames.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
        bool isGuest = me?.IsGuest ?? true;
        bool friendsMayVisit = await _visits.FriendsMayVisitAsync(playerId, cancellationToken).ConfigureAwait(false);
        FriendState state = await _friends.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Guid> friends = state.Friends;
        IReadOnlyList<Guid> incoming = state.Incoming;
        IReadOnlyList<Guid> outgoing = state.Outgoing;

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

        Guid targetId = target.Id;
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

        bool nowFriends;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FriendState mine = await _friends.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
            FriendState theirs = await _friends.GetAsync(targetId, cancellationToken).ConfigureAwait(false);
            if (mine.Friends.Contains(targetId))
            {
                return FriendStatus.AlreadyFriends;
            }

            if (mine.Outgoing.Contains(targetId))
            {
                return FriendStatus.AlreadyRequested;
            }

            if (mine.Friends.Count >= FriendRules.MaxFriends || theirs.Friends.Count >= FriendRules.MaxFriends)
            {
                return FriendStatus.ListFull;
            }

            DateTimeOffset now = _time.GetUtcNow();
            nowFriends = mine.Incoming.Contains(targetId);
            if (!nowFriends && (mine.Outgoing.Count >= FriendRules.MaxPending || theirs.Incoming.Count >= FriendRules.MaxPending))
            {
                return FriendStatus.PendingFull;
            }

            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                if (nowFriends)
                {
                    // They already asked: asking back is accepting.
                    unit.Repository<FriendRequest>().Remove(new FriendRequest { FromId = targetId, ToId = playerId });
                    unit.Repository<Friendship>().Add(new Friendship { PlayerId = playerId, FriendId = targetId, CreatedAt = now });
                    unit.Repository<Friendship>().Add(new Friendship { PlayerId = targetId, FriendId = playerId, CreatedAt = now });
                }
                else
                {
                    unit.Repository<FriendRequest>().Add(new FriendRequest { FromId = playerId, ToId = targetId, CreatedAt = now });
                }

                try
                {
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (unit.IsUniqueViolation(ex) || unit.IsConcurrencyConflict(ex))
                {
                    // What the copy said was out of date: read both again.
                    _friends.Changed(playerId, targetId);
                    return nowFriends ? FriendStatus.AlreadyFriends : FriendStatus.AlreadyRequested;
                }
                catch
                {
                    _friends.Changed(playerId, targetId);
                    throw;
                }
            }

            if (nowFriends)
            {
                _friends.Apply(playerId, s => new FriendState([.. s.Friends, targetId], [.. s.Incoming.Where(id => id != targetId)], s.Outgoing));
                _friends.Apply(targetId, s => new FriendState([.. s.Friends, playerId], s.Incoming, [.. s.Outgoing.Where(id => id != playerId)]));
            }
            else
            {
                _friends.Apply(playerId, s => new FriendState(s.Friends, s.Incoming, [.. s.Outgoing, targetId]));
                _friends.Apply(targetId, s => new FriendState(s.Friends, [.. s.Incoming, playerId], s.Outgoing));
            }
        }
        finally
        {
            _ = _gate.Release();
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FriendState mine = await _friends.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (!mine.Incoming.Contains(fromId))
            {
                return FriendStatus.NoRequest;
            }

            FriendState theirs = await _friends.GetAsync(fromId, cancellationToken).ConfigureAwait(false);
            bool befriend = accept && !mine.Friends.Contains(fromId);
            if (accept && (mine.Friends.Count >= FriendRules.MaxFriends || theirs.Friends.Count >= FriendRules.MaxFriends))
            {
                return FriendStatus.ListFull;
            }

            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                unit.Repository<FriendRequest>().Remove(new FriendRequest { FromId = fromId, ToId = playerId });
                if (befriend)
                {
                    DateTimeOffset now = _time.GetUtcNow();
                    unit.Repository<Friendship>().Add(new Friendship { PlayerId = playerId, FriendId = fromId, CreatedAt = now });
                    unit.Repository<Friendship>().Add(new Friendship { PlayerId = fromId, FriendId = playerId, CreatedAt = now });
                }

                try
                {
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (unit.IsUniqueViolation(ex) || unit.IsConcurrencyConflict(ex))
                {
                    _friends.Changed(playerId, fromId);
                    return FriendStatus.Conflict;
                }
                catch
                {
                    _friends.Changed(playerId, fromId);
                    throw;
                }
            }

            _friends.Apply(playerId, s => new FriendState(befriend ? [.. s.Friends, fromId] : s.Friends, [.. s.Incoming.Where(id => id != fromId)], s.Outgoing));
            _friends.Apply(fromId, s => new FriendState(befriend ? [.. s.Friends, playerId] : s.Friends, s.Incoming, [.. s.Outgoing.Where(id => id != playerId)]));
        }
        finally
        {
            _ = _gate.Release();
        }

        await this.PushSnapshotAsync(playerId, cancellationToken).ConfigureAwait(false);
        await this.PushSnapshotAsync(fromId, cancellationToken).ConfigureAwait(false);
        return FriendStatus.Ok;
    }

    /// <inheritdoc/>
    public async Task<FriendStatus> RemoveAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken = default)
    {
        bool wasFriends;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FriendState mine = await _friends.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
            FriendState theirs = await _friends.GetAsync(otherId, cancellationToken).ConfigureAwait(false);
            bool mineListsThem = mine.Friends.Contains(otherId);
            bool theirsListsMe = theirs.Friends.Contains(playerId);
            bool iAsked = mine.Outgoing.Contains(otherId);
            bool theyAsked = mine.Incoming.Contains(otherId);
            wasFriends = mineListsThem || theirsListsMe;
            if (!wasFriends && !iAsked && !theyAsked)
            {
                return FriendStatus.NotFriends;
            }

            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                if (mineListsThem)
                {
                    unit.Repository<Friendship>().Remove(new Friendship { PlayerId = playerId, FriendId = otherId });
                }

                if (theirsListsMe)
                {
                    unit.Repository<Friendship>().Remove(new Friendship { PlayerId = otherId, FriendId = playerId });
                }

                if (iAsked)
                {
                    unit.Repository<FriendRequest>().Remove(new FriendRequest { FromId = playerId, ToId = otherId });
                }

                if (theyAsked)
                {
                    unit.Repository<FriendRequest>().Remove(new FriendRequest { FromId = otherId, ToId = playerId });
                }

                try
                {
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (unit.IsConcurrencyConflict(ex))
                {
                    _friends.Changed(playerId, otherId);
                    return FriendStatus.Conflict;
                }
                catch
                {
                    _friends.Changed(playerId, otherId);
                    throw;
                }
            }

            _friends.Apply(playerId, s => new FriendState([.. s.Friends.Where(id => id != otherId)], [.. s.Incoming.Where(id => id != otherId)], [.. s.Outgoing.Where(id => id != otherId)]));
            _friends.Apply(otherId, s => new FriendState([.. s.Friends.Where(id => id != playerId)], [.. s.Incoming.Where(id => id != playerId)], [.. s.Outgoing.Where(id => id != playerId)]));
        }
        finally
        {
            _ = _gate.Release();
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
        PlayerFact? owner = await _nicknames.FindAsync(ownerId, cancellationToken).ConfigureAwait(false);
        if (owner is null || owner.IsGuest)
        {
            return FriendStatus.GuestNotAllowed;
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
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
            finally
            {
                _visits.SettingsChanged(ownerId);
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
        => (await _friends.GetFriendsAsync(a, cancellationToken).ConfigureAwait(false)).Contains(b);

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

        IReadOnlyList<Guid> friends = await _friends.GetFriendsAsync(playerId, CancellationToken.None).ConfigureAwait(false);

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
