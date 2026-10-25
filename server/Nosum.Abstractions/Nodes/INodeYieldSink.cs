// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.Nodes;

/// <summary>Work that must commit with a delivery: the node row's change, in the same unit of work. Return <see langword="false"/> to abort.</summary>
/// <param name="unit">The unit of work the delivery commits with.</param>
/// <param name="cancellationToken">Cancels the operation.</param>
/// <returns><see langword="true"/> to commit.</returns>
public delegate Task<bool> NodeYieldEnlistment(IUnitOfWork unit, CancellationToken cancellationToken);

/// <summary>What a breaking node gives, and to whom.</summary>
/// <param name="PlayerId">The player who broke it.</param>
/// <param name="Instance">The place it happened in.</param>
/// <param name="TileX">The node's tile x, where a drop would appear.</param>
/// <param name="TileY">The node's tile y.</param>
/// <param name="Items">What it gave; may be empty.</param>
/// <param name="Reason">Audit reason, for example <c>node.stone</c>.</param>
public sealed record NodeYieldRequest(Guid PlayerId, MapInstance Instance, int TileX, int TileY, IReadOnlyList<ItemSlot> Items, string Reason);

/// <summary>Result of a delivery.</summary>
public enum NodeYieldStatus
{
    /// <summary>Delivered, and the enlisted work committed with it.</summary>
    Ok = 0,

    /// <summary>The player has no room; nothing changed, the node stays as it was.</summary>
    NoSpace = 1,

    /// <summary>Another change got in the way; nothing changed, try again.</summary>
    Conflict = 2,

    /// <summary>The delivery was refused for another reason; nothing changed.</summary>
    Refused = 3,
}

/// <summary>
/// The one place a breaking node's yield leaves the node system (docs/adr/ADR-021 D5). Today it puts the items into the breaker's inventory
/// through the transfer service; ground items replace it by dropping them at the tile. Either way the enlisted work (changing or deleting the node's row)
/// commits with the delivery, so a node is never broken without its yield or yielded twice.
/// </summary>
public interface INodeYieldSink
{
    /// <summary>Delivers a node's yield.</summary>
    /// <param name="request">What and to whom.</param>
    /// <param name="enlist">Work that commits in the same unit of work; <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<NodeYieldStatus> DeliverAsync(NodeYieldRequest request, NodeYieldEnlistment? enlist = null, CancellationToken cancellationToken = default);
}
