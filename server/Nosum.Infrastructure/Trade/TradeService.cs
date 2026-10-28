// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Chat;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Trade;
using Nosum.Contracts.Trade;
using Nosum.Contracts.Trade.S2C;
using Nosum.Gameplay.Trade;

namespace Nosum.Infrastructure.Trade;

/// <summary>
/// <see cref="ITradeService"/>: invitations and open sessions in memory, the swap as one <see cref="ITransferService"/> call. The
/// confirm rules are <see cref="TradeSession"/>. A single gate orders every state change (trade traffic is low and a swap is one
/// short transaction); leaving a map or disconnecting ends the player's trade (docs/adr/ADR-013).
/// </summary>
public sealed class TradeService : ITradeService, IDisposable
{
    /// <summary>How long an unanswered invitation stays open.</summary>
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromSeconds(30);

    private readonly IMapPresence _presence;
    private readonly IInventoryService _inventory;
    private readonly ITransferService _transfers;
    private readonly IPlayerNotifier _notifier;
    private readonly IBlockList? _blocks;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, Invite> _invitesByInvitee = [];
    private readonly Dictionary<Guid, Guid> _inviteeByInviter = [];
    private readonly Dictionary<Guid, Open> _open = [];

    /// <summary>Initializes the service and starts listening for players leaving maps.</summary>
    /// <param name="presence">Who is on which map; both parties must share one.</param>
    /// <param name="inventory">Reads what a player holds when they offer it.</param>
    /// <param name="transfers">Runs the swap.</param>
    /// <param name="notifier">Pushes invitations, state and the end to the parties.</param>
    /// <param name="time">The clock for invitation expiry; tests pass a fake one.</param>
    /// <param name="blocks">The block lists: a trade request between players where either blocked the other is dropped (docs/adr/ADR-029 D3); <see langword="null"/> to check none.</param>
    public TradeService(IMapPresence presence, IInventoryService inventory, ITransferService transfers, IPlayerNotifier notifier, TimeProvider time, IBlockList? blocks = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(time);

        _presence = presence;
        _inventory = inventory;
        _transfers = transfers;
        _notifier = notifier;
        _time = time;
        _blocks = blocks;
        _presence.PlayerLeft += this.OnPlayerLeftAsync;
    }

    /// <inheritdoc/>
    public async Task<TradeStatus> RequestAsync(IConnection connection, Guid playerId, uint targetMapPlayerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? me))
        {
            return TradeStatus.NotOnMap;
        }

        // "Same map" is the presence group: the target must be among the players the presence reports for my map.
        List<PlayerPresence> players = [];
        _presence.CopyPlayers(me.Key, players);
        PlayerPresence? target = players.Find(p => p.PlayerId == targetMapPlayerId);
        if (target is null)
        {
            return TradeStatus.TargetNotFound;
        }

        if (target.AccountId == playerId)
        {
            return TradeStatus.SelfTrade;
        }

        // A block in either direction drops the request; it looks like the other player is not there (docs/adr/ADR-029 D3).
        if (_blocks is not null && await _blocks.IsBlockedEitherWayAsync(playerId, target.AccountId, cancellationToken).ConfigureAwait(false))
        {
            return TradeStatus.TargetNotFound;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await this.DropExpiredInvitesAsync(cancellationToken).ConfigureAwait(false);
            if (this.IsBusy(playerId))
            {
                return TradeStatus.AlreadyTrading;
            }

            if (this.IsBusy(target.AccountId))
            {
                return TradeStatus.TargetBusy;
            }

            _invitesByInvitee[target.AccountId] = new Invite(playerId, me.PlayerId, target.PlayerId, _time.GetUtcNow() + InviteLifetime);
            _inviteeByInviter[playerId] = target.AccountId;
            using PacketScope<TradeInvite> lease = PacketFactory<TradeInvite>.Acquire();
            lease.Value.FromPlayerId = me.PlayerId;
            _ = await _notifier.TrySendAsync(target.AccountId, lease.Value, cancellationToken).ConfigureAwait(false);
            return TradeStatus.Ok;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<TradeStatus> RespondAsync(Guid playerId, bool accept, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await this.DropExpiredInvitesAsync(cancellationToken).ConfigureAwait(false);
            if (!_invitesByInvitee.Remove(playerId, out Invite? invite))
            {
                return TradeStatus.NoInvite;
            }

            _ = _inviteeByInviter.Remove(invite.Inviter);
            if (!accept)
            {
                await this.PushEndedAsync(invite.Inviter, TradeEndReason.Declined, cancellationToken).ConfigureAwait(false);
                return TradeStatus.Ok;
            }

            Open open = new(new TradeSession(invite.Inviter, playerId), invite.InviterMapId, invite.InviteeMapId);
            _open[invite.Inviter] = open;
            _open[playerId] = open;
            await this.PushStateAsync(open, cancellationToken).ConfigureAwait(false);
            return TradeStatus.Ok;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<TradeStatus> SetOfferAsync(Guid playerId, IReadOnlyList<TradeLine> lines, long coins, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        TradeOffer offer = new(lines, coins);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_open.TryGetValue(playerId, out Open? open))
            {
                return TradeStatus.NoSession;
            }

            if (offer.Validate() != TradeVerdict.Ok)
            {
                return TradeStatus.InvalidOffer;
            }

            // The offer is a claim, checked now so the UI is honest and again, authoritatively, inside the swap transaction.
            InventoryView held = await _inventory.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
            foreach (TradeLine line in offer.Lines)
            {
                int have = 0;
                foreach (InventorySlotView slot in held.Slots)
                {
                    if (slot.ItemId == line.ItemId)
                    {
                        have += slot.Quantity;
                    }
                }

                if (have < line.Quantity)
                {
                    return TradeStatus.NotEnoughItems;
                }
            }

            if (offer.Coins > 0 && await _transfers.GetBalanceAsync(playerId, cancellationToken).ConfigureAwait(false) < offer.Coins)
            {
                return TradeStatus.NotEnoughCoins;
            }

            if (open.Session.SetOffer(playerId, offer) != TradeVerdict.Ok)
            {
                return TradeStatus.InvalidOffer;
            }

            await this.PushStateAsync(open, cancellationToken).ConfigureAwait(false);
            return TradeStatus.Ok;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<TradeStatus> ConfirmAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_open.TryGetValue(playerId, out Open? open))
            {
                return TradeStatus.NoSession;
            }

            TradeVerdict verdict = open.Session.Confirm(playerId);
            if (verdict != TradeVerdict.Ok)
            {
                return verdict == TradeVerdict.NothingOffered ? TradeStatus.NothingOffered : TradeStatus.NoSession;
            }

            if (!open.Session.IsReady)
            {
                await this.PushStateAsync(open, cancellationToken).ConfigureAwait(false);
                return TradeStatus.Ok;
            }

            TransferStatus swap = await this.SwapAsync(open.Session, cancellationToken).ConfigureAwait(false);
            this.Remove(open);
            TradeEndReason reason = swap switch
            {
                TransferStatus.Ok => TradeEndReason.Completed,
                TransferStatus.NotEnoughItems => TradeEndReason.NotEnoughItems,
                TransferStatus.NotEnoughCoins => TradeEndReason.NotEnoughCoins,
                TransferStatus.NoSpace => TradeEndReason.NoSpace,
                _ => TradeEndReason.Failed,
            };
            await this.PushEndedAsync(open.Session.A, reason, cancellationToken).ConfigureAwait(false);
            await this.PushEndedAsync(open.Session.B, reason, cancellationToken).ConfigureAwait(false);
            return TradeStatus.Ok;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<TradeStatus> CancelAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await this.DropExpiredInvitesAsync(cancellationToken).ConfigureAwait(false);
            if (_open.TryGetValue(playerId, out Open? open))
            {
                this.Remove(open);
                await this.PushEndedAsync(playerId, TradeEndReason.Cancelled, cancellationToken).ConfigureAwait(false);
                await this.PushEndedAsync(open.Session.OtherOf(playerId), TradeEndReason.CancelledByOther, cancellationToken).ConfigureAwait(false);
                return TradeStatus.Ok;
            }

            if (_invitesByInvitee.Remove(playerId, out Invite? received))
            {
                _ = _inviteeByInviter.Remove(received.Inviter);
                await this.PushEndedAsync(received.Inviter, TradeEndReason.Declined, cancellationToken).ConfigureAwait(false);
                return TradeStatus.Ok;
            }

            if (_inviteeByInviter.Remove(playerId, out Guid invitee))
            {
                _ = _invitesByInvitee.Remove(invitee);
                await this.PushEndedAsync(playerId, TradeEndReason.Cancelled, cancellationToken).ConfigureAwait(false);
                await this.PushEndedAsync(invitee, TradeEndReason.CancelledByOther, cancellationToken).ConfigureAwait(false);
                return TradeStatus.Ok;
            }

            return TradeStatus.NoSession;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _presence.PlayerLeft -= this.OnPlayerLeftAsync;
        _gate.Dispose();
    }

    private async Task<TransferStatus> SwapAsync(TradeSession session, CancellationToken cancellationToken)
    {
        TransferParty a = TransferParty.Player(session.A);
        TransferParty b = TransferParty.Player(session.B);
        TradeOffer offerA = session.OfferOf(session.A);
        TradeOffer offerB = session.OfferOf(session.B);

        List<TransferLeg> legs = [];
        foreach (TradeLine line in offerA.Lines)
        {
            legs.Add(new TransferLeg(a, line.ItemId, -line.Quantity));
            legs.Add(new TransferLeg(b, line.ItemId, line.Quantity));
        }

        foreach (TradeLine line in offerB.Lines)
        {
            legs.Add(new TransferLeg(b, line.ItemId, -line.Quantity));
            legs.Add(new TransferLeg(a, line.ItemId, line.Quantity));
        }

        // Both players take part even when only one side offers items, and coins settle as one net amount each.
        legs.Add(new TransferLeg(a, 0, 0, offerB.Coins - offerA.Coins));
        legs.Add(new TransferLeg(b, 0, 0, offerA.Coins - offerB.Coins));
        return await _transfers.ExecuteAsync(legs, "trade.swap", null, cancellationToken).ConfigureAwait(false);
    }

    private bool IsBusy(Guid player) => _open.ContainsKey(player) || _invitesByInvitee.ContainsKey(player) || _inviteeByInviter.ContainsKey(player);

    private void Remove(Open open)
    {
        open.Session.Close();
        _ = _open.Remove(open.Session.A);
        _ = _open.Remove(open.Session.B);
    }

    private async Task DropExpiredInvitesAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        List<Guid>? expired = null;
        foreach (KeyValuePair<Guid, Invite> pair in _invitesByInvitee)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                (expired ??= []).Add(pair.Key);
            }
        }

        if (expired is null)
        {
            return;
        }

        foreach (Guid invitee in expired)
        {
            if (_invitesByInvitee.Remove(invitee, out Invite? invite))
            {
                _ = _inviteeByInviter.Remove(invite.Inviter);
                await this.PushEndedAsync(invite.Inviter, TradeEndReason.InviteExpired, cancellationToken).ConfigureAwait(false);
                await this.PushEndedAsync(invitee, TradeEndReason.InviteExpired, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Raised from a disconnect or map change; there is no caller to report to, and the trade must end regardless.")]
    private async ValueTask OnPlayerLeftAsync(PlayerPresence player)
    {
        try
        {
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Guid id = player.AccountId;
                if (_open.TryGetValue(id, out Open? open))
                {
                    this.Remove(open);
                    await this.PushEndedAsync(id, TradeEndReason.SelfLeft, CancellationToken.None).ConfigureAwait(false);
                    await this.PushEndedAsync(open.Session.OtherOf(id), TradeEndReason.OtherLeft, CancellationToken.None).ConfigureAwait(false);
                }

                if (_invitesByInvitee.Remove(id, out Invite? received))
                {
                    _ = _inviteeByInviter.Remove(received.Inviter);
                    await this.PushEndedAsync(received.Inviter, TradeEndReason.OtherLeft, CancellationToken.None).ConfigureAwait(false);
                }

                if (_inviteeByInviter.Remove(id, out Guid invitee))
                {
                    _ = _invitesByInvitee.Remove(invitee);
                    await this.PushEndedAsync(invitee, TradeEndReason.OtherLeft, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                _ = _gate.Release();
            }
        }
        catch (Exception)
        {
            // The sessions of a vanished player are dropped by the next request that touches them.
        }
    }

    private async Task PushEndedAsync(Guid playerId, TradeEndReason reason, CancellationToken cancellationToken)
    {
        using PacketScope<TradeEnded> lease = PacketFactory<TradeEnded>.Acquire();
        lease.Value.Reason = reason;
        _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task PushStateAsync(Open open, CancellationToken cancellationToken)
    {
        await this.PushStateToAsync(open, open.Session.A, cancellationToken).ConfigureAwait(false);
        await this.PushStateToAsync(open, open.Session.B, cancellationToken).ConfigureAwait(false);
    }

    private async Task PushStateToAsync(Open open, Guid receiver, CancellationToken cancellationToken)
    {
        TradeSession session = open.Session;
        Guid other = session.OtherOf(receiver);
        using PacketScope<TradeState> lease = PacketFactory<TradeState>.Acquire();
        TradeState state = lease.Value;
        state.OtherPlayerId = receiver == session.A ? open.MapIdB : open.MapIdA;
        Fill(state.MyItems, session.OfferOf(receiver));
        Fill(state.OtherItems, session.OfferOf(other));
        state.MyCoins = session.OfferOf(receiver).Coins;
        state.OtherCoins = session.OfferOf(other).Coins;
        state.MyConfirmed = session.IsConfirmed(receiver);
        state.OtherConfirmed = session.IsConfirmed(other);
        _ = await _notifier.TrySendAsync(receiver, state, cancellationToken).ConfigureAwait(false);
    }

    private static void Fill(List<TradeItemRow> rows, TradeOffer offer)
    {
        rows.Clear();
        foreach (TradeLine line in offer.Lines)
        {
            TradeItemRow row = new() { ItemId = line.ItemId, Quantity = line.Quantity };
            rows.Add(row);
        }
    }

    private sealed record Invite(Guid Inviter, uint InviterMapId, uint InviteeMapId, DateTimeOffset ExpiresAt);

    private sealed record Open(TradeSession Session, uint MapIdA, uint MapIdB);
}
