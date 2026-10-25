// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Community;
using Nosum.Contracts.Goals;
using Nosum.Contracts.Goals.C2S;
using Nosum.Contracts.Goals.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Goals;

/// <summary>The community goals at the Town board (docs/adr/ADR-030). The server counts every delivery and pays every reward; a handler only relays the intent.</summary>
[PacketHandler("Nosum.Goals")]
public static partial class GoalHandlers
{
    [Inject] private static ICommunityGoalService s_goals = null!;

    /// <summary>Replies with the goals of the season and the player's part.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(GoalOpCode.GetGoals)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 4, PolicyId = "goals-read")]
    public static async ValueTask HandleGetGoalsAsync(IPacketContext<GetGoals> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        GoalsView view = await s_goals.GetAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetGoals, GoalsSnapshot>(context, sequence, r =>
        {
            r.ServerNowMs = view.ServerNowMs;
            r.SeasonEndsAtMs = view.SeasonEndsAtMs;
            r.Rows.Clear();
            foreach (GoalView goal in view.Goals)
            {
                GoalRow row = GoalRow.Create();
                row.GoalId = goal.GoalId;
                row.SeasonNumber = goal.SeasonNumber;
                row.Total = goal.Total;
                row.State = goal.State;
                row.Mine = goal.Mine;
                row.Contributors = goal.Contributors;
                r.Rows.Add(row);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Delivers items into a goal.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(GoalOpCode.DeliverToGoal)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "goals-deliver")]
    public static async ValueTask HandleDeliverToGoalAsync(IPacketContext<DeliverToGoal> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int goalId = context.Packet.GoalId;
        int quantity = context.Packet.Quantity;
        PlayerIdentity player = context.Connection.GetPlayer();

        DeliverOutcome outcome = await s_goals.DeliverAsync(context.Connection, player.PlayerId, goalId, quantity, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<DeliverToGoal, DeliverResult>(context, sequence, r =>
        {
            r.Status = outcome.Status;
            r.Delivered = outcome.Delivered;
        }).ConfigureAwait(false);
    }
}
