// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Requests;

namespace Nosum.Abstractions.Requests;

/// <summary>One request of today as the server reports it to a player.</summary>
/// <param name="Slot">The request's slot in its day.</param>
/// <param name="ItemId">The item to deliver.</param>
/// <param name="Quantity">How many items.</param>
/// <param name="RewardCoins">The coins the delivery pays.</param>
/// <param name="Filled">Whether the viewer already filled it today.</param>
public sealed record RequestView(int Slot, int ItemId, int Quantity, long RewardCoins, bool Filled);

/// <summary>The requests a player sees at the board.</summary>
/// <param name="ServerNowMs">The server clock when read, Unix milliseconds.</param>
/// <param name="Day">The game day number the requests belong to.</param>
/// <param name="DayEndsAtMs">When the day ends, Unix milliseconds.</param>
/// <param name="Requests">Today's requests, by slot.</param>
/// <param name="EarlyUntilMs">When the early bonus of today ends, Unix milliseconds (docs/adr/ADR-036 D10).</param>
/// <param name="EarlyBonusPercent">The share the early bonus adds, in percent.</param>
public sealed record RequestsView(long ServerNowMs, int Day, long DayEndsAtMs, IReadOnlyList<RequestView> Requests, long EarlyUntilMs, int EarlyBonusPercent);

/// <summary>Result of a delivery.</summary>
/// <param name="Status">Ok, or why nothing was taken.</param>
/// <param name="Reward">The coins that were paid, the early bonus included; zero unless Ok.</param>
/// <param name="Bonus">Whether the delivery was in the early window and was paid the bonus.</param>
public readonly record struct RequestOutcome(RequestStatus Status, long Reward, bool Bonus = false);

/// <summary>
/// The Town requests board (docs/adr/ADR-036): a few server-wide delivery requests per game day, each fillable once per player. A delivery moves the items
/// out of the player and the coins in, and records the fill, in one transaction.
/// </summary>
public interface IRequestService
{
    /// <summary>Reads today's requests and the viewer's fills; the day's requests are written first when nobody has asked yet.</summary>
    /// <param name="playerId">The viewer.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Today's requests.</returns>
    Task<RequestsView> GetAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Fills a request of today. The player must stand at the Town board.</summary>
    /// <param name="connection">The player's connection (their position is the server's own record).</param>
    /// <param name="playerId">The player.</param>
    /// <param name="day">The game day the player saw the request on.</param>
    /// <param name="slot">The request's slot in that day.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome.</returns>
    Task<RequestOutcome> DeliverAsync(IConnection connection, Guid playerId, int day, int slot, CancellationToken cancellationToken = default);

    /// <summary>Writes the requests of a game day if they are missing and deletes the days older than the retention. The day clock calls it; reads do the same lazily.</summary>
    /// <param name="day">The game day number.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the day's rows exist.</returns>
    Task EnsureDayAsync(int day, CancellationToken cancellationToken = default);
}
