// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Visit.S2C;

/// <summary>One visitor's watering on one game day of the owner's farm. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GuestbookRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static GuestbookRow Create() => new();

    /// <summary>The visitor's account id.</summary>
    [SerializeOrder(0)] public Guid VisitorId { get; set; }

    /// <summary>The visitor's nickname as stored (a guest has the neutral <c>guest-1234</c> form).</summary>
    [SerializeOrder(1)] public string Nickname { get; set; } = string.Empty;

    /// <summary>The game day number (<c>GameCalendar.DayNumberAt</c>) the crops were watered on.</summary>
    [SerializeOrder(2)] public int DayNumber { get; set; }

    /// <summary>How many crops the visitor watered that day.</summary>
    [SerializeOrder(3)] public int Waters { get; set; }

    /// <summary>Whether the entry was new when the guestbook was read.</summary>
    [SerializeOrder(4)] public bool Unread { get; set; }
}
