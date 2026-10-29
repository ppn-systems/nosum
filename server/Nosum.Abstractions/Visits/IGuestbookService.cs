// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Visit.S2C;

namespace Nosum.Abstractions.Visits;

/// <summary>One visitor's watering on one day of the owner's farm, as the guestbook reads it.</summary>
/// <param name="VisitorId">The visitor's account id.</param>
/// <param name="Nickname">The visitor's stored nickname.</param>
/// <param name="DayNumber">The game day number.</param>
/// <param name="Waters">How many crops were watered that day.</param>
/// <param name="Unread">Whether the entry was new before this read.</param>
public sealed record GuestbookLine(Guid VisitorId, string Nickname, int DayNumber, int Waters, bool Unread);

/// <summary>What one read of the guestbook returns: how many entries were new, and the newest rows.</summary>
/// <param name="UnreadCount">Every entry that was new before the read, not only the rows shown.</param>
/// <param name="Rows">The newest days, at most <c>GuestbookRules.MaxRows</c>.</param>
public sealed record GuestbookView(int UnreadCount, IReadOnlyList<GuestbookLine> Rows)
{
    /// <summary>Copies the view into a snapshot packet (the packet's list is cleared first).</summary>
    /// <param name="packet">The packet to fill.</param>
    public void FillSnapshot(GuestbookSnapshot packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        packet.UnreadCount = this.UnreadCount;
        packet.Rows.Clear();
        foreach (GuestbookLine line in this.Rows)
        {
            packet.Rows.Add(new GuestbookRow { VisitorId = line.VisitorId, Nickname = line.Nickname, DayNumber = line.DayNumber, Waters = line.Waters, Unread = line.Unread });
        }
    }
}

/// <summary>
/// The farm guestbook (docs/adr/ADR-066): counts a visitor's watering into the owner's log, and lets the owner read it. Only the
/// farm's own watering path calls <see cref="RecordWateringAsync"/>; the write happens after the crop was changed and never changes what the owner has.
/// </summary>
public interface IGuestbookService
{
    /// <summary>Counts one more crop a visitor watered on the owner's farm on a day, and marks the entry new.</summary>
    /// <param name="ownerId">The owner of the farm.</param>
    /// <param name="visitorId">The visitor who watered.</param>
    /// <param name="dayNumber">The game day number of the watering.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the count is saved.</returns>
    Task RecordWateringAsync(Guid ownerId, Guid visitorId, int dayNumber, CancellationToken cancellationToken = default);

    /// <summary>Reads the owner's guestbook and marks every entry read.</summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entries that were new and the newest rows.</returns>
    Task<GuestbookView> ReadAsync(Guid ownerId, CancellationToken cancellationToken = default);
}
