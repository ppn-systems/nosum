// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.Presence;

/// <summary>
/// Decides who may enter a map instance. A shared map is open to everybody; a private one to its owner and to the players its owner let in
/// (the allow-list and the co-op setting, docs/adr/ADR-019). Asked on every entry into a private place; callers do not change when the rule does.
/// </summary>
public interface IInstanceAccessPolicy
{
    /// <summary>Returns whether a player may enter an instance.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="playerId">The entering player's account id.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> when entry is allowed.</returns>
    ValueTask<bool> CanEnterAsync(MapInstance instance, Guid playerId, CancellationToken cancellationToken = default);
}
