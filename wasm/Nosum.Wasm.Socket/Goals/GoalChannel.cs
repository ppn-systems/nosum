// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Goals;
using Nosum.Contracts.Goals.C2S;
using Nosum.Contracts.Goals.S2C;

namespace Nosum.Wasm.Socket.Goals;

/// <summary>One goal as the client mirrors it. The target, item and reward are the shared <c>CommunityGoalCatalog</c>'s.</summary>
/// <param name="GoalId">The goal id.</param>
/// <param name="SeasonNumber">The season number of the goal row.</param>
/// <param name="Total">How many items everybody delivered.</param>
/// <param name="State">Where the goal is in its life.</param>
/// <param name="Mine">How many items this player delivered.</param>
/// <param name="Contributors">How many players delivered anything.</param>
public readonly record struct GoalInfo(int GoalId, int SeasonNumber, long Total, GoalState State, long Mine, int Contributors);

/// <summary>The goals of the season as the server last said, with the clock they were read at.</summary>
/// <param name="ServerNowMs">The server clock when read, Unix milliseconds.</param>
/// <param name="SeasonEndsAtMs">When the season ends, Unix milliseconds on the server clock; zero before the first read.</param>
/// <param name="Goals">The goals, in catalogue order.</param>
public sealed record GoalsInfo(long ServerNowMs, long SeasonEndsAtMs, IReadOnlyList<GoalInfo> Goals)
{
    /// <summary>Gets the state before the first read.</summary>
    public static readonly GoalsInfo Empty = new(0, 0, []);
}

/// <summary>
/// The community goals' side of the connection (docs/adr/ADR-030): reads the goals when the board page opens, delivers items, and applies the
/// progress the server pushes to everyone in the Town. The server decides every outcome; the channel only mirrors it.
/// </summary>
public sealed class GoalChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable _progress;
    private readonly Lock _gate = new();
    private GoalsInfo _goals = GoalsInfo.Empty;

    /// <summary>Initializes the channel and starts listening for progress pushes.</summary>
    /// <param name="socket">The game connection.</param>
    public GoalChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _progress = socket.Subscribe<GoalProgress>(this.OnProgress);
    }

    /// <summary>Raised when the goals changed (a read or a push). May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised with the server's answer to a delivery: the status and how many items were taken. May run on any thread.</summary>
    public event Action<GoalStatus, int>? Delivered;

    /// <summary>Gets the goals as last read or pushed.</summary>
    public GoalsInfo Goals
    {
        get
        {
            lock (_gate)
            {
                return _goals;
            }
        }
    }

    /// <summary>Reads the goals of the season and this player's part in them.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <summary>Delivers items into a goal.</summary>
    /// <param name="goalId">The goal.</param>
    /// <param name="quantity">How many items.</param>
    public void Deliver(int goalId, int quantity) => _ = this.DeliverAsync(goalId, quantity);

    /// <inheritdoc/>
    public void Dispose() => _progress.Dispose();

    [SuppressMessage("Design", "CA1031", Justification = "A lost read changes nothing; the player opens the board again.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetGoals> request = PacketFactory<GetGoals>.Acquire();
            GoalsSnapshot reply = await _socket.RequestAsync<GoalsSnapshot>(request.Value).ConfigureAwait(false);
            List<GoalInfo> goals = new(reply.Rows.Count);
            foreach (GoalRow row in reply.Rows)
            {
                goals.Add(new GoalInfo(row.GoalId, row.SeasonNumber, row.Total, row.State, row.Mine, row.Contributors));
            }

            lock (_gate)
            {
                _goals = new GoalsInfo(reply.ServerNowMs, reply.SeasonEndsAtMs, goals);
            }

            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task DeliverAsync(int goalId, int quantity)
    {
        try
        {
            using PacketScope<DeliverToGoal> request = PacketFactory<DeliverToGoal>.Acquire();
            request.Value.GoalId = goalId;
            request.Value.Quantity = quantity;
            DeliverResult reply = await _socket.RequestAsync<DeliverResult>(request.Value).ConfigureAwait(false);
            this.Delivered?.Invoke(reply.Status, reply.Delivered);
            if (reply.Status == GoalStatus.Ok)
            {
                // The push carries the total; this player's own part comes with a read.
                await this.LoadAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    // Applies a push to the mirror. A goal the mirror has not read yet (a new season's) is read instead.
    private void OnProgress(GoalProgress push)
    {
        bool known = false;
        lock (_gate)
        {
            List<GoalInfo> goals = [.. _goals.Goals];
            for (int i = 0; i < goals.Count; i++)
            {
                if (goals[i].GoalId == push.GoalId)
                {
                    goals[i] = goals[i] with { SeasonNumber = push.SeasonNumber, Total = push.Total, State = push.State, Contributors = push.Contributors };
                    known = true;
                }
            }

            if (known)
            {
                _goals = _goals with { Goals = goals };
            }
        }

        if (known)
        {
            this.Changed?.Invoke();
        }
        else if (this.Goals.SeasonEndsAtMs > 0)
        {
            this.Load();
        }
    }
}
