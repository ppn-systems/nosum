// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Visit;
using Nosum.Contracts.Visit.C2S;
using Nosum.Contracts.Visit.S2C;

namespace Nosum.Wasm.Socket.Visit;

/// <summary>A player in the owner's visitors list.</summary>
/// <param name="PlayerId">The player's account id.</param>
/// <param name="Nickname">The nickname as the server sent it.</param>
/// <param name="Listed">Whether the owner put them on the allow-list.</param>
/// <param name="Present">Whether they are in the owner's place now.</param>
public readonly record struct VisitorInfo(Guid PlayerId, string Nickname, bool Listed, bool Present);

/// <summary>The player's own visiting settings and visitors as the server last sent them.</summary>
/// <param name="CoopMembersMayVisit">Whether members of the player's co-op may visit.</param>
/// <param name="IsGuest">Whether the player is a guest, who keeps no allow-list.</param>
/// <param name="Visitors">The allow-list, plus any co-op member visiting now.</param>
public sealed record VisitorsInfo(bool CoopMembersMayVisit, bool IsGuest, IReadOnlyList<VisitorInfo> Visitors)
{
    /// <summary>Gets the state before the first read.</summary>
    public static readonly VisitorsInfo None = new(true, false, []);
}

/// <summary>One visitor's watering on one day of the player's farm.</summary>
/// <param name="VisitorId">The visitor's account id.</param>
/// <param name="Nickname">The visitor's nickname as the server sent it.</param>
/// <param name="DayNumber">The game day number.</param>
/// <param name="Waters">How many crops the visitor watered that day.</param>
/// <param name="Unread">Whether the entry was new before the last read.</param>
public readonly record struct GuestbookEntryInfo(Guid VisitorId, string Nickname, int DayNumber, int Waters, bool Unread);

/// <summary>The player's farm guestbook as the server last sent it.</summary>
/// <param name="UnreadCount">How many entries were new before the last read, not only the ones listed.</param>
/// <param name="Entries">The newest days, newest first.</param>
public sealed record GuestbookInfo(int UnreadCount, IReadOnlyList<GuestbookEntryInfo> Entries)
{
    /// <summary>Gets the state before the first read.</summary>
    public static readonly GuestbookInfo None = new(0, []);
}

/// <summary>
/// The visiting side of the connection: reads the player's allow-list and sends the requests that edit it or ask to visit someone.
/// The server decides every outcome. A visit that was accepted arrives as the usual map-change push, which the map scene already handles.
/// </summary>
public sealed class VisitChannel
{
    private readonly IGameSocket _socket;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public VisitChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Raised when the list changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised with the server's answer to a request. May run on any thread.</summary>
    public event Action<VisitStatus>? Completed;

    /// <summary>Raised when a request to visit someone was accepted: the player is on their way. May run on any thread.</summary>
    public event Action? Visiting;

    /// <summary>Gets the player's own visiting settings and visitors.</summary>
    public VisitorsInfo Current { get; private set; } = VisitorsInfo.None;

    /// <summary>Gets the player's farm guestbook as the server last sent it.</summary>
    public GuestbookInfo Guestbook { get; private set; } = GuestbookInfo.None;

    /// <summary>Reads the allow-list.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <summary>Reads the farm guestbook. The server marks the entries it sends as read.</summary>
    public void LoadGuestbook() => _ = this.LoadGuestbookAsync();

    /// <summary>Lets a player visit.</summary>
    /// <param name="nickname">Their nickname as typed.</param>
    public void Add(string nickname)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        _ = this.SendAsync<AddVisitor>(r => r.Nickname = nickname, reload: true);
    }

    /// <summary>Takes a player off the allow-list and sends them out.</summary>
    /// <param name="playerId">The player's account id.</param>
    public void Remove(Guid playerId) => _ = this.SendAsync<RemoveVisitor>(r => r.PlayerId = playerId, reload: true);

    /// <summary>Sends a player out of the place for now.</summary>
    /// <param name="playerId">The player's account id.</param>
    public void Kick(Guid playerId) => _ = this.SendAsync<KickVisitor>(r => r.PlayerId = playerId, reload: true);

    /// <summary>Turns "co-op members may visit" on or off.</summary>
    /// <param name="allowed">The new setting.</param>
    public void SetCoopVisits(bool allowed) => _ = this.SendAsync<SetCoopVisits>(r => r.Allowed = allowed, reload: true);

    /// <summary>Asks to visit another player's farm.</summary>
    /// <param name="nickname">The owner's nickname as typed.</param>
    public void Visit(string nickname)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        _ = this.SendAsync<VisitPlayer>(r => r.Nickname = nickname, reload: false);
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried when the panel opens again; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetVisitors> request = PacketFactory<GetVisitors>.Acquire();
            VisitorsSnapshot snapshot = await _socket.RequestAsync<VisitorsSnapshot>(request.Value).ConfigureAwait(false);
            List<VisitorInfo> visitors = new(snapshot.Visitors.Count);
            foreach (VisitorRow row in snapshot.Visitors)
            {
                visitors.Add(new VisitorInfo(row.PlayerId, row.Nickname, row.Listed, row.Present));
            }

            this.Current = new VisitorsInfo(snapshot.CoopMembersMayVisit, snapshot.IsGuest, visitors);
            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried when the panel opens again; there is no caller to report to.")]
    private async Task LoadGuestbookAsync()
    {
        try
        {
            using PacketScope<GetGuestbook> request = PacketFactory<GetGuestbook>.Acquire();
            GuestbookSnapshot snapshot = await _socket.RequestAsync<GuestbookSnapshot>(request.Value).ConfigureAwait(false);
            List<GuestbookEntryInfo> entries = new(snapshot.Rows.Count);
            foreach (GuestbookRow row in snapshot.Rows)
            {
                entries.Add(new GuestbookEntryInfo(row.VisitorId, row.Nickname, row.DayNumber, row.Waters, row.Unread));
            }

            this.Guestbook = new GuestbookInfo(snapshot.UnreadCount, entries);
            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task SendAsync<TRequest>(Action<TRequest> fill, bool reload)
        where TRequest : Nalix.Codec.DataFrames.PacketBase<TRequest>, Nalix.Abstractions.Networking.Packets.IPacketStaticOpcode, new()
    {
        try
        {
            using PacketScope<TRequest> request = PacketFactory<TRequest>.Acquire();
            fill(request.Value);
            VisitResult reply = await _socket.RequestAsync<VisitResult>(request.Value).ConfigureAwait(false);
            if (!reload && reply.Status == VisitStatus.Ok)
            {
                this.Visiting?.Invoke();
            }

            this.Completed?.Invoke(reply.Status);
            if (reload)
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
