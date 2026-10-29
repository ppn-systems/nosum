// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Trade;
using Nosum.Contracts.Trade.C2S;
using Nosum.Contracts.Trade.S2C;

namespace Nosum.Wasm.Socket.Trade;

/// <summary>One item line of an offer.</summary>
/// <param name="ItemId">Item id.</param>
/// <param name="Quantity">How many.</param>
public readonly record struct TradeLineInfo(int ItemId, int Quantity);

/// <summary>The open trade as the server last pushed it.</summary>
/// <param name="OtherPlayerId">The other party's id on the map.</param>
/// <param name="MyItems">The player's offered items.</param>
/// <param name="MyCoins">The player's offered coins.</param>
/// <param name="OtherItems">The other party's offered items.</param>
/// <param name="OtherCoins">The other party's offered coins.</param>
/// <param name="MyConfirmed">Whether the player confirmed.</param>
/// <param name="OtherConfirmed">Whether the other party confirmed.</param>
/// <param name="ConfirmUnlocksAt">The local tick count (<see cref="Environment.TickCount64"/>) when confirming opens again; zero or earlier when it is open.</param>
public sealed record TradeInfo(uint OtherPlayerId, IReadOnlyList<TradeLineInfo> MyItems, long MyCoins, IReadOnlyList<TradeLineInfo> OtherItems, long OtherCoins, bool MyConfirmed, bool OtherConfirmed, long ConfirmUnlocksAt);

/// <summary>The trade's side of the connection: invitations, the open session, and how it ended. The server owns every offer and decides the swap.</summary>
public sealed class TradeChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable[] _subscriptions;

    /// <summary>Initializes the channel and starts listening for trade pushes.</summary>
    /// <param name="socket">The game connection.</param>
    public TradeChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _subscriptions =
        [
            socket.Subscribe<TradeInvite>(p =>
            {
                this.Invite = p.FromPlayerId;
                this.Changed?.Invoke();
            }),
            socket.Subscribe<TradeState>(this.OnState),
            socket.Subscribe<TradeEnded>(p =>
            {
                TradeEndReason reason = p.Reason;
                this.Open = null;
                this.Invite = 0;
                this.Waiting = false;
                this.Ended?.Invoke(reason);
                this.Changed?.Invoke();
            }),
        ];
    }

    /// <summary>Raised when the invitation, the open trade or the waiting flag changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised when the trade or an invitation ended. May run on any thread.</summary>
    public event Action<TradeEndReason>? Ended;

    /// <summary>Raised with the server's answer to a trade request. May run on any thread.</summary>
    public event Action<TradeStatus>? Completed;

    /// <summary>Gets the id of the player who invited this player, or zero.</summary>
    public uint Invite { get; private set; }

    /// <summary>Gets whether this player invited someone who has not answered.</summary>
    public bool Waiting { get; private set; }

    /// <summary>Gets the open trade, or <see langword="null"/>.</summary>
    public TradeInfo? Open { get; private set; }

    /// <summary>Invites a player on the map.</summary>
    /// <param name="playerId">The other player's id on the map.</param>
    public void Request(uint playerId) => _ = this.RequestAsync(playerId);

    /// <summary>Answers the invitation.</summary>
    /// <param name="accept">Whether to accept.</param>
    public void Respond(bool accept)
    {
        this.Invite = 0;
        this.Changed?.Invoke();
        _ = this.RespondAsync(accept);
    }

    /// <summary>Replaces the player's offer.</summary>
    /// <param name="lines">The offered items.</param>
    /// <param name="coins">The offered coins.</param>
    public void SetOffer(IReadOnlyList<TradeLineInfo> lines, long coins)
    {
        ArgumentNullException.ThrowIfNull(lines);
        _ = this.SetOfferAsync(lines, coins);
    }

    /// <summary>Confirms the offers as they stand.</summary>
    public void Confirm() => _ = this.SimpleAsync<TradeConfirm>();

    /// <summary>Cancels the trade or the invitation.</summary>
    public void Cancel() => _ = this.SimpleAsync<TradeCancel>();

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    private static List<TradeLineInfo> Lines(List<TradeItemRow> rows)
    {
        List<TradeLineInfo> lines = new(rows.Count);
        foreach (TradeItemRow row in rows)
        {
            lines.Add(new TradeLineInfo(row.ItemId, row.Quantity));
        }

        return lines;
    }

    private void OnState(TradeState state)
    {
        this.Open = new TradeInfo(state.OtherPlayerId, Lines(state.MyItems), state.MyCoins, Lines(state.OtherItems), state.OtherCoins, state.MyConfirmed, state.OtherConfirmed, Environment.TickCount64 + state.ConfirmLockMs);
        this.Waiting = false;
        this.Invite = 0;
        this.Changed?.Invoke();
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task RequestAsync(uint playerId)
    {
        try
        {
            using PacketScope<TradeRequest> request = PacketFactory<TradeRequest>.Acquire();
            request.Value.TargetPlayerId = playerId;
            TradeResult reply = await _socket.RequestAsync<TradeResult>(request.Value).ConfigureAwait(false);
            if (reply.Status == TradeStatus.Ok)
            {
                this.Waiting = true;
                this.Changed?.Invoke();
            }

            this.Completed?.Invoke(reply.Status);
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task RespondAsync(bool accept)
    {
        try
        {
            using PacketScope<TradeRespond> request = PacketFactory<TradeRespond>.Acquire();
            request.Value.Accept = accept;
            TradeResult reply = await _socket.RequestAsync<TradeResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(reply.Status);
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task SetOfferAsync(IReadOnlyList<TradeLineInfo> lines, long coins)
    {
        try
        {
            using PacketScope<TradeSetOffer> request = PacketFactory<TradeSetOffer>.Acquire();
            request.Value.Items.Clear();
            foreach (TradeLineInfo line in lines)
            {
                request.Value.Items.Add(new TradeItemRow { ItemId = line.ItemId, Quantity = line.Quantity });
            }

            request.Value.Coins = coins;
            TradeResult reply = await _socket.RequestAsync<TradeResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(reply.Status);
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task SimpleAsync<TRequest>()
        where TRequest : Nalix.Codec.DataFrames.PacketBase<TRequest>, Nalix.Abstractions.Networking.Packets.IPacketStaticOpcode, new()
    {
        try
        {
            using PacketScope<TRequest> request = PacketFactory<TRequest>.Acquire();
            TradeResult reply = await _socket.RequestAsync<TradeResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(reply.Status);
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }
}
