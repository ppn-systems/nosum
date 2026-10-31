// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Abstractions.Player;

/// <summary>
/// Spends the energy a running player used (docs/adr/ADR-081 D2). The movement path accumulates the energy owed and calls this with whole units,
/// in the background, so a move never waits for the database.
/// </summary>
public interface IRunEnergy
{
    /// <summary>Spends energy, and pushes the new energy to the player, when the player has it.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="energy">Whole energy units to spend, at least one.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the energy was spent; <see langword="false"/> when the player lacks it (nothing changed).</returns>
    Task<bool> TrySpendAsync(Guid playerId, int energy, CancellationToken cancellationToken = default);
}
