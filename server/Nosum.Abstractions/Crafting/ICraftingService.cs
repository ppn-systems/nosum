// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Placeable;

namespace Nosum.Abstractions.Crafting;

/// <summary>
/// Crafting (docs/adr/ADR-024 D1): the server checks that the recipe is unlocked for the player, then removes the inputs and adds the outputs in one
/// atomic, audited inventory change. Crafting works anywhere, from the inventory; there is no workbench.
/// </summary>
public interface ICraftingService
{
    /// <summary>Crafts a recipe.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="recipeId">Recipe id.</param>
    /// <param name="times">How many crafts, 1 to 99.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status and how many crafts were made.</returns>
    Task<CraftOutcome> CraftAsync(IConnection connection, int recipeId, int times, CancellationToken cancellationToken = default);

    /// <summary>Lists the recipes the connection's player has unlocked.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The recipe ids; empty for a connection that is not signed in on a map.</returns>
    Task<IReadOnlyList<int>> UnlockedAsync(IConnection connection, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="ICraftingService.CraftAsync"/>.</summary>
/// <param name="Status">Ok, or why nothing was made.</param>
/// <param name="Times">How many crafts were made; zero on a refusal.</param>
public readonly record struct CraftOutcome(CraftStatus Status, int Times);
