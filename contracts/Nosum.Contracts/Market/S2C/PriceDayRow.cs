// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Market.S2C;

/// <summary>The sales of one item on one game day. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PriceDayRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static PriceDayRow Create() => new();

    /// <summary>The game day number (<c>GameCalendar.DayNumberAt</c>).</summary>
    [SerializeOrder(0)] public int Day { get; set; }

    /// <summary>The lowest unit price of the day, coins.</summary>
    [SerializeOrder(1)] public long Min { get; set; }

    /// <summary>The median unit price of the day, coins.</summary>
    [SerializeOrder(2)] public long Median { get; set; }

    /// <summary>The highest unit price of the day, coins.</summary>
    [SerializeOrder(3)] public long Max { get; set; }

    /// <summary>How many sales the day had.</summary>
    [SerializeOrder(4)] public int Sales { get; set; }
}
