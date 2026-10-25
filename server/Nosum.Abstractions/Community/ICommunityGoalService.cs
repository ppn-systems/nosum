// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Goals;

namespace Nosum.Abstractions.Community;

/// <summary>One goal as the server reports it to a player.</summary>
/// <param name="GoalId">The goal (<c>CommunityGoalCatalog</c>).</param>
/// <param name="SeasonNumber">The season number of the goal row.</param>
/// <param name="Total">How many items everybody delivered.</param>
/// <param name="State">Where the goal is in its life.</param>
/// <param name="Mine">How many items the viewer delivered.</param>
/// <param name="Contributors">How many players delivered anything.</param>
public sealed record GoalView(int GoalId, int SeasonNumber, long Total, GoalState State, long Mine, int Contributors);

/// <summary>The goals a player sees at the board.</summary>
/// <param name="ServerNowMs">The server clock when read, Unix milliseconds.</param>
/// <param name="SeasonEndsAtMs">When the current season ends, Unix milliseconds.</param>
/// <param name="Goals">The goals, in catalogue order.</param>
public sealed record GoalsView(long ServerNowMs, long SeasonEndsAtMs, IReadOnlyList<GoalView> Goals);

/// <summary>Result of a delivery.</summary>
/// <param name="Status">Ok, or why nothing was taken.</param>
/// <param name="Delivered">How many items were taken; zero unless Ok.</param>
public readonly record struct DeliverOutcome(GoalStatus Status, int Delivered);

/// <summary>
/// Community goals (docs/adr/ADR-030): the whole server delivers a crop at the Town board to unlock a shared reward. A delivery moves the items out
/// of the player and into the goal's counters in one transaction; completion is permanent, pays the contributors once, and sets the goal's unlock
/// flag; a goal that is still open when its season ends expires and pays a consolation.
/// </summary>
public interface ICommunityGoalService
{
    /// <summary>Reads the goals of the current season and the completed ones of other seasons, with the viewer's part.</summary>
    /// <param name="playerId">The viewer.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The goals.</returns>
    Task<GoalsView> GetAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Delivers items from the player's inventory into a goal. The player must stand at the Town board; more than the goal still takes delivers what fits.</summary>
    /// <param name="connection">The player's connection (their position is the server's own record).</param>
    /// <param name="playerId">The player.</param>
    /// <param name="goalId">The goal.</param>
    /// <param name="quantity">How many items.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome.</returns>
    Task<DeliverOutcome> DeliverAsync(IConnection connection, Guid playerId, int goalId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Expires the goals of seasons that ended without reaching their target, then pays what is owed. The day clock calls it; it is also run at start.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many goals were expired.</returns>
    Task<int> CloseEndedSeasonsAsync(CancellationToken cancellationToken = default);

    /// <summary>Pays every contribution of a completed or expired goal that was not paid yet (a crash or a refused payment left it). Each is paid once.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many contributions were paid.</returns>
    Task<int> SettlePendingAsync(CancellationToken cancellationToken = default);
}
