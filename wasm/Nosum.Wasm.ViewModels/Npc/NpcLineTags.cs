// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.ViewModels.Npc;

/// <summary>
/// The moments a merchant's line can fit (docs/adr/ADR-080 D2). A moment is one value per group: a season, a weather, a time of day and the
/// farmer's progress (any of the progress flags that are true). A line tagged with a group fits only when the moment has one of its values in
/// that group; a line with no tag in a group fits whatever the moment is there.
/// </summary>
[Flags]
public enum NpcLineTags : ushort
{
    /// <summary>Any moment.</summary>
    None = 0,

    /// <summary>Spring.</summary>
    Spring = 1 << 0,

    /// <summary>Summer.</summary>
    Summer = 1 << 1,

    /// <summary>Fall.</summary>
    Fall = 1 << 2,

    /// <summary>Winter.</summary>
    Winter = 1 << 3,

    /// <summary>A clear day.</summary>
    Sun = 1 << 4,

    /// <summary>Rain or a storm.</summary>
    Wet = 1 << 5,

    /// <summary>Snow.</summary>
    Snow = 1 << 6,

    /// <summary>Five to ten o'clock.</summary>
    Morning = 1 << 7,

    /// <summary>Eleven to four o'clock.</summary>
    Day = 1 << 8,

    /// <summary>Five o'clock in the evening to five in the morning.</summary>
    Evening = 1 << 9,

    /// <summary>No quest finished yet: a new farmer.</summary>
    Newcomer = 1 << 10,

    /// <summary>The first harvest quest is finished.</summary>
    Harvested = 1 << 11,

    /// <summary>The farmer has reached the mine's tenth floor.</summary>
    DeepMiner = 1 << 12,

    /// <summary>The season values.</summary>
    SeasonGroup = Spring | Summer | Fall | Winter,

    /// <summary>The weather values.</summary>
    WeatherGroup = Sun | Wet | Snow,

    /// <summary>The time-of-day values.</summary>
    TimeGroup = Morning | Day | Evening,

    /// <summary>The progress flags.</summary>
    ProgressGroup = Newcomer | Harvested | DeepMiner,
}

/// <summary>One line of a merchant's pool: its number (the key is <c>Npc_{Id}_Line_{nn}</c>) and the moments it fits.</summary>
/// <param name="Number">The line's number, from 1.</param>
/// <param name="Tags">The moments it fits; none for any.</param>
public readonly record struct NpcLine(int Number, NpcLineTags Tags = NpcLineTags.None);
