// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Abstractions.Shops;

/// <summary>
/// Development-only tester tool: gives a player items and/or coins without paying. The host registers it, and the handler that exposes it,
/// only when it runs in the Development environment; in Production neither exists, so nothing can reach it.
/// </summary>
public interface IDevGrantService
{
    /// <summary>Gives items and/or coins in one transfer (audit reason <c>dev.grant</c>).</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="itemId">The item, or zero for coins only.</param>
    /// <param name="quantity">How many items, 1 to a stack; ignored without an item.</param>
    /// <param name="coins">Coins to add, 0 to 1 000 000 000.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when granted; <see langword="false"/> for an invalid request or when nothing fits.</returns>
    Task<bool> GrantAsync(Guid playerId, int itemId, int quantity, long coins, CancellationToken cancellationToken = default);
}
