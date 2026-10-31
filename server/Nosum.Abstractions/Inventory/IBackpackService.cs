// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Inventory;

namespace Nosum.Abstractions.Inventory;

/// <summary>
/// The tailor's backpack upgrades (docs/adr/ADR-081). A bigger backpack is bought at the tailor's counter for coins; the server prices it with
/// <c>BackpackRules</c>, and the coins and the new size are saved in one transaction. The position is the server's own record, never a packet value.
/// </summary>
public interface IBackpackService
{
    /// <summary>Reads the player's backpack size and the next upgrade.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The state; a player who is not at the counter gets <see cref="BackpackStatus.NotAtCounter"/>.</returns>
    Task<BackpackView> GetAsync(IConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Buys the next backpack size.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome and the state after the attempt.</returns>
    Task<BackpackView> UpgradeAsync(IConnection connection, CancellationToken cancellationToken = default);
}

/// <summary>A player's backpack as read.</summary>
/// <param name="Status">Ok, or why a request was refused.</param>
/// <param name="Capacity">Slots the backpack has now; zero when the request was refused for the counter.</param>
/// <param name="NextCapacity">Slots the next size has; zero when the backpack is already the largest.</param>
/// <param name="NextPrice">Coins the next size costs; zero when there is none.</param>
public readonly record struct BackpackView(BackpackStatus Status, int Capacity, int NextCapacity, int NextPrice);
