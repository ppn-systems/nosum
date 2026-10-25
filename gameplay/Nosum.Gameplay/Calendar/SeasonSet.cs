// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Calendar;

/// <summary>A set of seasons, used as data: the seasons a crop grows in.</summary>
[Flags]
public enum SeasonSet
{
    /// <summary>No season.</summary>
    None = 0,

    /// <summary>Spring.</summary>
    Spring = 1 << 0,

    /// <summary>Summer.</summary>
    Summer = 1 << 1,

    /// <summary>Fall.</summary>
    Fall = 1 << 2,

    /// <summary>Winter.</summary>
    Winter = 1 << 3,

    /// <summary>Every season.</summary>
    All = Spring | Summer | Fall | Winter,
}

/// <summary>Helpers for <see cref="SeasonSet"/>.</summary>
public static class SeasonSets
{
    /// <summary>Returns the set holding only one season.</summary>
    /// <param name="season">The season.</param>
    /// <returns>The single-season set.</returns>
    public static SeasonSet Of(Season season) => (SeasonSet)(1 << (int)season);

    /// <summary>Returns whether a set contains a season.</summary>
    /// <param name="set">The set.</param>
    /// <param name="season">The season.</param>
    /// <returns><see langword="true"/> when the season is in the set.</returns>
    public static bool Contains(this SeasonSet set, Season season) => (set & Of(season)) != SeasonSet.None;
}
