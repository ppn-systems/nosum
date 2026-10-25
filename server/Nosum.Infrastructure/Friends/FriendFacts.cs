// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Domain.Social;
using Nosum.Infrastructure.Caching;

namespace Nosum.Infrastructure.Friends;

/// <summary>A player's friends and open friend requests.</summary>
/// <param name="Friends">The friends, oldest friendship first.</param>
/// <param name="Incoming">The players who asked this player, oldest first.</param>
/// <param name="Outgoing">The players this player asked, oldest first.</param>
public sealed record FriendState(IReadOnlyList<Guid> Friends, IReadOnlyList<Guid> Incoming, IReadOnlyList<Guid> Outgoing)
{
    /// <summary>Gets the state of a player who has no friends and no requests.</summary>
    public static FriendState Empty { get; } = new([], [], []);
}

/// <summary>
/// Friends and friend requests per player, read through a cache (docs/adr/ADR-035 D5). <c>FriendService</c> and the block list are the only writers of the two tables:
/// the friend service replaces the entries of the two players it changed with what it saved, a block drops them. The visit access check and the online-status
/// announcements read the same entries, so none of them reads the tables on its own.
/// </summary>
public sealed class FriendFacts
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ReadThroughCache<Guid, IReadOnlyList<Guid>> _friends;
    private readonly ReadThroughCache<Guid, Requests> _requests;

    /// <summary>Initializes the facts.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="cached">Whether the facts are kept; <see langword="false"/> reads the rows every time.</param>
    public FriendFacts(IUnitOfWorkFactory unitOfWork, bool cached = true)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
        _friends = new ReadThroughCache<Guid, IReadOnlyList<Guid>>(cached);
        _requests = new ReadThroughCache<Guid, Requests>(cached);
    }

    /// <summary>Gets a player's friends and requests: one read for each the first time, memory afterwards.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The state.</returns>
    public async ValueTask<FriendState> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> friends = await this.GetFriendsAsync(playerId, cancellationToken).ConfigureAwait(false);
        Requests requests = await _requests.GetAsync(playerId, ct => this.LoadRequestsAsync(playerId, ct), cancellationToken).ConfigureAwait(false);
        return new FriendState(friends, requests.Incoming, requests.Outgoing);
    }

    /// <summary>Gets only a player's friends, oldest friendship first (the online-status announcements and the access check need no requests).</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The friends.</returns>
    public ValueTask<IReadOnlyList<Guid>> GetFriendsAsync(Guid playerId, CancellationToken cancellationToken = default)
        => _friends.GetAsync(playerId, ct => this.LoadFriendsAsync(playerId, ct), cancellationToken);

    /// <summary>Drops what is known about players whose friends or requests changed in a way the caller does not track.</summary>
    /// <param name="playerIds">The players.</param>
    public void Changed(params Guid[] playerIds)
    {
        ArgumentNullException.ThrowIfNull(playerIds);
        foreach (Guid player in playerIds)
        {
            _friends.Invalidate(player);
            _requests.Invalidate(player);
        }
    }

    /// <summary>Changes the held entries of a player to what the caller just saved; an entry that is not held stays unheld and is read later.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="change">Maps the held state to the saved one.</param>
    public void Apply(Guid playerId, Func<FriendState, FriendState> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _friends.Update(playerId, held => change(new FriendState(held, [], [])).Friends);
        _requests.Update(playerId, held =>
        {
            FriendState next = change(new FriendState([], held.Incoming, held.Outgoing));
            return new Requests(next.Incoming, next.Outgoing);
        });
    }

    private async Task<IReadOnlyList<Guid>> LoadFriendsAsync(Guid playerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<Friendship> friends = await unit.Repository<Friendship>().ListAsync(f => f.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
            return [.. friends.OrderBy(f => f.CreatedAt).Select(f => f.FriendId)];
        }
    }

    private async Task<Requests> LoadRequestsAsync(Guid playerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<FriendRequest> requests = await unit.Repository<FriendRequest>().ListAsync(r => r.FromId == playerId || r.ToId == playerId, cancellationToken).ConfigureAwait(false);
            return new Requests(
                [.. requests.Where(r => r.ToId == playerId).OrderBy(r => r.CreatedAt).Select(r => r.FromId)],
                [.. requests.Where(r => r.FromId == playerId).OrderBy(r => r.CreatedAt).Select(r => r.ToId)]);
        }
    }

    private sealed record Requests(IReadOnlyList<Guid> Incoming, IReadOnlyList<Guid> Outgoing);
}
