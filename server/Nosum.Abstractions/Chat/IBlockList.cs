// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Chat;

namespace Nosum.Abstractions.Chat;

/// <summary>One blocked player as listed to the owner.</summary>
/// <param name="PlayerId">The blocked player's account id.</param>
/// <param name="Nickname">The blocked player's nickname.</param>
public sealed record BlockedView(Guid PlayerId, string Nickname);

/// <summary>
/// Per-player block lists (docs/adr/ADR-029 D3). A blocked player's chat, trade and visit requests are dropped server-side; the other
/// services ask this one. The lists are kept in memory once read (the server is a single process, docs/adr/ADR-009), so the question
/// "did A block B" on the chat path costs no query.
/// </summary>
public interface IBlockList
{
    /// <summary>Tells whether <paramref name="ownerId"/> blocked <paramref name="otherId"/>.</summary>
    /// <param name="ownerId">The player who keeps the list.</param>
    /// <param name="otherId">The player who may be on it.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the owner blocked the other player.</returns>
    ValueTask<bool> HasBlockedAsync(Guid ownerId, Guid otherId, CancellationToken cancellationToken = default);

    /// <summary>Tells whether either player blocked the other.</summary>
    /// <param name="a">One player.</param>
    /// <param name="b">The other player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when a block exists in either direction.</returns>
    ValueTask<bool> IsBlockedEitherWayAsync(Guid a, Guid b, CancellationToken cancellationToken = default);

    /// <summary>Lists the player's block list, oldest first.</summary>
    /// <param name="ownerId">The player who keeps the list.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The blocked players with their nicknames.</returns>
    Task<IReadOnlyList<BlockedView>> ListAsync(Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>Blocks a player by nickname; the friendship and open friend requests between the two end.</summary>
    /// <param name="ownerId">The player who keeps the list.</param>
    /// <param name="nickname">The nickname to block.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<ChatStatus> BlockAsync(Guid ownerId, string nickname, CancellationToken cancellationToken = default);

    /// <summary>Takes a player off the block list.</summary>
    /// <param name="ownerId">The player who keeps the list.</param>
    /// <param name="nickname">The nickname to unblock.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<ChatStatus> UnblockAsync(Guid ownerId, string nickname, CancellationToken cancellationToken = default);
}
