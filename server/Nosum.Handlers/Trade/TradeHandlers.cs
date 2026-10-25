// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Trade;
using Nosum.Contracts.Trade;
using Nosum.Contracts.Trade.C2S;
using Nosum.Contracts.Trade.S2C;
using Nosum.Gameplay.Trade;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Trade;

/// <summary>Direct trade between two players on the same map (docs/adr/ADR-013). The identity comes from the connection; every outcome is the service's.</summary>
[PacketHandler("Nosum.Trade")]
public static partial class TradeHandlers
{
    [Inject] private static ITradeService s_trades = null!;

    /// <summary>Invites a player on the same map to trade.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(TradeOpCode.TradeRequest)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "trade-request")]
    public static async ValueTask HandleTradeRequestAsync(IPacketContext<TradeRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        uint target = context.Packet.TargetPlayerId;
        PlayerIdentity player = context.Connection.GetPlayer();

        TradeStatus status = await s_trades.RequestAsync(context.Connection, player.PlayerId, target, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<TradeRequest, TradeResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Accepts or declines an invitation.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(TradeOpCode.TradeRespond)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "trade-request")]
    public static async ValueTask HandleTradeRespondAsync(IPacketContext<TradeRespond> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        bool accept = context.Packet.Accept;
        PlayerIdentity player = context.Connection.GetPlayer();

        TradeStatus status = await s_trades.RespondAsync(player.PlayerId, accept, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<TradeRespond, TradeResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Replaces the player's side of the trade.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(TradeOpCode.TradeSetOffer)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(10, 4, PolicyId = "trade-offer")]
    public static async ValueTask HandleTradeSetOfferAsync(IPacketContext<TradeSetOffer> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        List<TradeLine> lines = [];
        foreach (TradeItemRow row in context.Packet.Items)
        {
            lines.Add(new TradeLine(row.ItemId, row.Quantity));
        }

        long coins = context.Packet.Coins;
        PlayerIdentity player = context.Connection.GetPlayer();

        TradeStatus status = await s_trades.SetOfferAsync(player.PlayerId, lines, coins, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<TradeSetOffer, TradeResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Confirms the offers as they stand; the swap runs when both sides did.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(TradeOpCode.TradeConfirm)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(6, 3, PolicyId = "trade-confirm")]
    public static async ValueTask HandleTradeConfirmAsync(IPacketContext<TradeConfirm> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        TradeStatus status = await s_trades.ConfirmAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<TradeConfirm, TradeResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Cancels the trade or declines the invitation.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(TradeOpCode.TradeCancel)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "trade-confirm")]
    public static async ValueTask HandleTradeCancelAsync(IPacketContext<TradeCancel> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        TradeStatus status = await s_trades.CancelAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<TradeCancel, TradeResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }
}
