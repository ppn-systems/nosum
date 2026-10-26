// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;

namespace Nosum.Abstractions.Accounts;

/// <summary>Loads and saves where a player was last, so the server can put them back there.</summary>
public interface IPlayerStateService
{
    /// <summary>Loads a player's saved state.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The saved state, or null when none was ever saved (the caller picks the spawn point).</returns>
    Task<PlayerStateRow?> LoadAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>Saves a player's state, replacing any earlier one.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="state">The state to store.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the state is stored.</returns>
    Task SaveAsync(Guid playerId, PlayerStateRow state, CancellationToken cancellationToken);

    /// <summary>Saves a player's state as they leave a map: written to the row, and it never adds an in-memory copy (a held copy follows the row).</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="state">The state to store.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the state is stored.</returns>
    Task LeaveAsync(Guid playerId, PlayerStateRow state, CancellationToken cancellationToken);
}

/// <summary>A player's saved position.</summary>
/// <param name="MapName">The map the player is on.</param>
/// <param name="X">Feet x, world pixels.</param>
/// <param name="Y">Feet y, world pixels.</param>
/// <param name="Facing">The direction the player faces.</param>
public readonly record struct PlayerStateRow(string MapName, double X, double Y, Facing Facing);
