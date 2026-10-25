// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nalix.SDK.Transport.Extensions;
using Nosum.Contracts.Goals;
using Nosum.Contracts.Goals.C2S;
using Nosum.Contracts.Goals.S2C;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Market.S2C;

namespace Nosum.IntegrationTests.Server;

/// <summary>A goal progress push as a client received it.</summary>
internal sealed record GoalPushCopy(int GoalId, int SeasonNumber, long Total, GoalState State, int Contributors);

/// <summary>The goals snapshot as a client received it.</summary>
internal sealed record GoalsCopy(long ServerNowMs, long SeasonEndsAtMs, GoalRow[] Rows);

/// <summary>The community goal and price history requests of the wire tests (docs/adr/ADR-030).</summary>
internal sealed partial class SocialClient
{
    private IDisposable? _goalSubscription;
    private int _goalPushCount;

    /// <summary>Gets the goal progress pushes this client received since it started listening.</summary>
    public Inbox<GoalPushCopy> GoalPushes { get; } = new();

    /// <summary>Gets how many goal progress pushes arrived.</summary>
    public int GoalPushCount => Volatile.Read(ref _goalPushCount);

    /// <summary>Starts recording <see cref="GoalProgress"/> pushes.</summary>
    public void ListenForGoalPushes()
        => _goalSubscription ??= _session.On<GoalProgress>(p =>
        {
            _ = Interlocked.Increment(ref _goalPushCount);
            this.GoalPushes.Add(new GoalPushCopy(p.GoalId, p.SeasonNumber, p.Total, p.State, p.Contributors));
        });

    public async Task<GoalsCopy> GetGoalsAsync(CancellationToken ct)
    {
        using GetGoals request = new();
        GoalsSnapshot reply = await this.RequestAsync<GoalsSnapshot>(request, ct);
        return new GoalsCopy(
            reply.ServerNowMs,
            reply.SeasonEndsAtMs,
            [.. reply.Rows.Select(row => new GoalRow { GoalId = row.GoalId, SeasonNumber = row.SeasonNumber, Total = row.Total, State = row.State, Mine = row.Mine, Contributors = row.Contributors })]);
    }

    public async Task<(GoalStatus Status, int Delivered)> DeliverToGoalAsync(int goalId, int quantity, CancellationToken ct)
    {
        using DeliverToGoal request = new() { GoalId = goalId, Quantity = quantity };
        DeliverResult result = await this.RequestAsync<DeliverResult>(request, ct);
        return (result.Status, result.Delivered);
    }

    public async Task<(int Today, PriceDayRow[] Days)> GetPriceHistoryAsync(int itemId, CancellationToken ct)
    {
        using GetPriceHistory request = new() { ItemId = itemId };
        PriceHistory reply = await this.RequestAsync<PriceHistory>(request, ct);
        PriceDayRow[] days = [.. reply.Days.Select(d => new PriceDayRow { Day = d.Day, Min = d.Min, Median = d.Median, Max = d.Max, Sales = d.Sales })];
        return (reply.Today, days);
    }
}
