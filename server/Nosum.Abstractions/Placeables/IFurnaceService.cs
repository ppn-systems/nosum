// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Placeable;

namespace Nosum.Abstractions.Placeables;

/// <summary>
/// Furnaces (docs/adr/ADR-024 D4): five ore and one coal become one bar after 30 game minutes. A job is two stored numbers (the ore and when it started);
/// whether it is ready is computed from the server clock, so nothing ticks. Only the owner of the place, or the members of the co-op on co-op land, use one.
/// </summary>
public interface IFurnaceService
{
    /// <summary>Starts a job.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="objectId">Id of the placed furnace.</param>
    /// <param name="oreItemId">The ore to smelt.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome.</returns>
    Task<FurnaceOutcome> StartAsync(IConnection connection, int objectId, int oreItemId, CancellationToken cancellationToken = default);

    /// <summary>Takes a finished bar into the inventory.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="objectId">Id of the placed furnace.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome, with the bar collected.</returns>
    Task<FurnaceOutcome> CollectAsync(IConnection connection, int objectId, CancellationToken cancellationToken = default);

    /// <summary>Reads the furnaces of the place the player is in that have a job.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The furnaces; empty when the player is on no placeable map.</returns>
    Task<IReadOnlyList<FurnaceView>> GetAsync(IConnection connection, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a furnace request.</summary>
/// <param name="Status">Ok, or why nothing changed.</param>
/// <param name="ObjectId">The furnace's id.</param>
/// <param name="ItemId">The bar collected; zero otherwise.</param>
/// <param name="Quantity">How many bars were collected; zero otherwise.</param>
public readonly record struct FurnaceOutcome(FurnaceStatus Status, int ObjectId, int ItemId, int Quantity);

/// <summary>A furnace with a job.</summary>
/// <param name="ObjectId">The furnace's id.</param>
/// <param name="State">Working or ready.</param>
/// <param name="OreItemId">The ore being smelted.</param>
/// <param name="RemainingMs">Milliseconds until the bar is ready; zero once it is.</param>
public readonly record struct FurnaceView(int ObjectId, FurnaceStateCode State, int OreItemId, int RemainingMs);
