// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Requests;
using Nosum.Contracts.Requests;
using Nosum.Contracts.Requests.C2S;
using Nosum.Contracts.Requests.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Requests;

/// <summary>The requests board at the Town board counter (docs/adr/ADR-036). The server rolls the requests, takes the items and pays the coins; a handler only relays the intent.</summary>
[PacketHandler("Nosum.Requests")]
public static partial class RequestHandlers
{
    [Inject] private static IRequestService s_requests = null!;

    /// <summary>Replies with today's requests and the player's fills.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(RequestsOpCode.GetRequests)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 4, PolicyId = "requests-read")]
    public static async ValueTask HandleGetRequestsAsync(IPacketContext<GetRequests> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        RequestsView view = await s_requests.GetAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetRequests, RequestsSnapshot>(context, sequence, r =>
        {
            r.ServerNowMs = view.ServerNowMs;
            r.Day = view.Day;
            r.DayEndsAtMs = view.DayEndsAtMs;
            r.EarlyUntilMs = view.EarlyUntilMs;
            r.EarlyBonusPercent = view.EarlyBonusPercent;
            r.Rows.Clear();
            foreach (RequestView request in view.Requests)
            {
                RequestRow row = RequestRow.Create();
                row.Slot = request.Slot;
                row.ItemId = request.ItemId;
                row.Quantity = request.Quantity;
                row.RewardCoins = request.RewardCoins;
                row.Filled = request.Filled;
                r.Rows.Add(row);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Fills one of today's requests.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(RequestsOpCode.DeliverRequest)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "requests-deliver")]
    public static async ValueTask HandleDeliverRequestAsync(IPacketContext<DeliverRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int day = context.Packet.Day;
        int slot = context.Packet.Slot;
        PlayerIdentity player = context.Connection.GetPlayer();

        RequestOutcome outcome = await s_requests.DeliverAsync(context.Connection, player.PlayerId, day, slot, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<DeliverRequest, RequestResult>(context, sequence, r =>
        {
            r.Status = outcome.Status;
            r.Reward = outcome.Reward;
            r.Bonus = outcome.Bonus;
        }).ConfigureAwait(false);
    }
}
