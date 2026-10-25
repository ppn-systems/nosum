// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Calendar;

/// <summary>The four seasons, in calendar order. Values are stable (docs/adr/ADR-018).</summary>
public enum Season
{
    /// <summary>Spring, the first season of the year.</summary>
    Spring = 0,

    /// <summary>Summer.</summary>
    Summer = 1,

    /// <summary>Fall (autumn).</summary>
    Fall = 2,

    /// <summary>Winter, the last season of the year.</summary>
    Winter = 3,
}
