// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Requests;
using Nosum.Contracts.Requests.C2S;
using Nosum.Contracts.Requests.S2C;

namespace Nosum.Wasm.Socket.Requests;

/// <summary>One request of today as the client mirrors it.</summary>
/// <param name="Slot">The request's slot in its day.</param>
/// <param name="ItemId">The item to deliver.</param>
/// <param name="Quantity">How many items.</param>
/// <param name="RewardCoins">The coins the delivery pays.</param>
/// <param name="Filled">Whether this player already filled it today.</param>
public readonly record struct RequestInfo(int Slot, int ItemId, int Quantity, long RewardCoins, bool Filled);

/// <summary>Today's requests as the server last said, with the clock they were read at.</summary>
/// <param name="ServerNowMs">The server clock when read, Unix milliseconds.</param>
/// <param name="Day">The game day number; zero before the first read.</param>
/// <param name="DayEndsAtMs">When the day ends, Unix milliseconds on the server clock.</param>
/// <param name="Requests">The requests, by slot.</param>
/// <param name="EarlyUntilMs">When the early bonus of the day ends, Unix milliseconds on the server clock.</param>
/// <param name="EarlyBonusPercent">The share the early bonus adds, in percent.</param>
public sealed record RequestsInfo(long ServerNowMs, int Day, long DayEndsAtMs, IReadOnlyList<RequestInfo> Requests, long EarlyUntilMs, int EarlyBonusPercent)
{
    /// <summary>Gets the state before the first read.</summary>
    public static readonly RequestsInfo Empty = new(0, 0, 0, [], 0, 0);
}

/// <summary>
/// The requests board's side of the connection (docs/adr/ADR-036): reads today's requests when the tab opens and delivers one. The server decides every
/// outcome; the channel only mirrors it. A board left open over midnight is refreshed by the first answer that is not Ok.
/// </summary>
public sealed class RequestChannel
{
    private readonly IGameSocket _socket;
    private readonly Lock _gate = new();
    private RequestsInfo _requests = RequestsInfo.Empty;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public RequestChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Raised when the requests changed (a read). May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised with the server's answer to a delivery: the status, the coins paid and whether the early bonus was paid. May run on any thread.</summary>
    public event Action<RequestStatus, long, bool>? Delivered;

    /// <summary>Gets today's requests as last read.</summary>
    public RequestsInfo Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests;
            }
        }
    }

    /// <summary>Reads today's requests and this player's fills.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <summary>Fills a request.</summary>
    /// <param name="day">The game day the request was shown for.</param>
    /// <param name="slot">The request's slot.</param>
    public void Deliver(int day, int slot) => _ = this.DeliverAsync(day, slot);

    [SuppressMessage("Design", "CA1031", Justification = "A lost read changes nothing; the player opens the tab again.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetRequests> request = PacketFactory<GetRequests>.Acquire();
            RequestsSnapshot reply = await _socket.RequestAsync<RequestsSnapshot>(request.Value).ConfigureAwait(false);
            List<RequestInfo> rows = new(reply.Rows.Count);
            foreach (RequestRow row in reply.Rows)
            {
                rows.Add(new RequestInfo(row.Slot, row.ItemId, row.Quantity, row.RewardCoins, row.Filled));
            }

            lock (_gate)
            {
                _requests = new RequestsInfo(reply.ServerNowMs, reply.Day, reply.DayEndsAtMs, rows, reply.EarlyUntilMs, reply.EarlyBonusPercent);
            }

            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task DeliverAsync(int day, int slot)
    {
        try
        {
            using PacketScope<DeliverRequest> request = PacketFactory<DeliverRequest>.Acquire();
            request.Value.Day = day;
            request.Value.Slot = slot;
            RequestResult reply = await _socket.RequestAsync<RequestResult>(request.Value).ConfigureAwait(false);
            this.Delivered?.Invoke(reply.Status, reply.Reward, reply.Bonus);

            // Ok changed this player's fills; NotToday, AlreadyFilled and Conflict mean the board was stale. Anything else leaves it as it was.
            if (reply.Status is RequestStatus.Ok or RequestStatus.NotToday or RequestStatus.AlreadyFilled or RequestStatus.Conflict)
            {
                await this.LoadAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }
}
