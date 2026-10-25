// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Abstractions.Nodes;

/// <summary>
/// What the server keeps per player about their tools: when the last swing started (the 300 ms cooldown, docs/adr/ADR-021 D8) and how much
/// water the watering can holds (D2). Held in memory: after a restart a can is full again.
/// </summary>
public interface IToolState
{
    /// <summary>Starts a swing when the previous one is over.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <returns><see langword="true"/> when the swing may happen; <see langword="false"/> while the cooldown runs.</returns>
    bool TryBeginSwing(Guid playerId);

    /// <summary>Gets the water in a player's can.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <returns>Water left; a player not seen yet has a full can.</returns>
    int WaterLeft(Guid playerId);

    /// <summary>Uses one unit of water and tells the player the new amount.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Water left, or -1 when the can was already empty.</returns>
    ValueTask<int> UseWaterAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Gets the most water the player's can holds: the capacity of the can they last filled (docs/adr/ADR-026 D1).</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <returns>The capacity; a player not seen yet has the basic can's.</returns>
    int CapacityOf(Guid playerId) => Nosum.Gameplay.Nodes.NodeLimits.WateringCanCapacity;

    /// <summary>Fills a can of a given capacity and tells the player (the capacity depends on the can's tier).</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="capacity">The capacity of the can being filled.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new amount, which is the capacity.</returns>
    ValueTask<int> RefillAsync(Guid playerId, int capacity, CancellationToken cancellationToken = default) => this.RefillAsync(playerId, cancellationToken);

    /// <summary>Fills the can and tells the player.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new amount, which is the capacity.</returns>
    ValueTask<int> RefillAsync(Guid playerId, CancellationToken cancellationToken = default);
}
