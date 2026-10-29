// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Visits;

/// <summary>
/// One visitor's watering on one game day of an owner's farm: a row of the farm guestbook (docs/adr/ADR-066). The key is the owner, the visitor and the day,
/// so a visitor's watering on a day is counted into one row.
/// </summary>
public sealed class GuestbookEntry
{
    /// <summary>Gets or sets the owner of the farm the crops are on.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Gets or sets the visitor who watered.</summary>
    public Guid VisitorId { get; set; }

    /// <summary>Gets or sets the game day number (<c>GameCalendar.DayNumberAt</c>).</summary>
    public int DayNumber { get; set; }

    /// <summary>Gets or sets how many crops the visitor watered that day.</summary>
    public int Waters { get; set; }

    /// <summary>Gets or sets whether the owner has not read the entry yet.</summary>
    public bool Unread { get; set; }

    /// <summary>Gets or sets when the row last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
