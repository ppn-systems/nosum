// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Placeables;

/// <summary>Where a furnace job stands.</summary>
public enum FurnaceState : byte
{
    /// <summary>Nothing is smelting.</summary>
    Idle = 0,

    /// <summary>Smelting; the bar is not ready yet.</summary>
    Working = 1,

    /// <summary>The bar is ready to be collected.</summary>
    Ready = 2,
}

/// <summary>
/// The furnace (docs/adr/ADR-024 D4): five ore and one coal become one bar after 30 game minutes. A job is two numbers, the ore smelted and the time it
/// started; whether it is ready is computed from the server clock and the calendar, never ticked.
/// </summary>
public static class FurnaceRules
{
    /// <summary>Ore one job consumes.</summary>
    public const int OreCost = 5;

    /// <summary>Coal one job consumes.</summary>
    public const int CoalCost = 1;

    /// <summary>Game minutes one job takes (a game minute is a thirtieth of a real minute at the default day length).</summary>
    public const int GameMinutes = 30;

    /// <summary>Tells which bar an ore smelts into.</summary>
    /// <param name="oreItemId">An item id.</param>
    /// <param name="barItemId">The bar when the item is smeltable ore.</param>
    /// <returns><see langword="true"/> for copper ore and iron ore.</returns>
    public static bool TryBarOf(int oreItemId, out int barItemId)
    {
        switch (oreItemId)
        {
            case ItemCatalog.CopperOre:
                barItemId = ItemCatalog.CopperBar;
                return true;
            case ItemCatalog.IronOre:
                barItemId = ItemCatalog.IronBar;
                return true;
            default:
                barItemId = 0;
                return false;
        }
    }

    /// <summary>Returns when a job started at <paramref name="startedAtMs"/> is ready.</summary>
    /// <param name="startedAtMs">Server time the job started, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar, which says how long a game minute lasts.</param>
    /// <returns>The ready time, Unix milliseconds.</returns>
    public static long ReadyAtMs(long startedAtMs, GameCalendar calendar)
    {
        System.ArgumentNullException.ThrowIfNull(calendar);

        return startedAtMs + (GameMinutes * calendar.DayLengthMs / GameCalendar.MinutesPerDay);
    }

    /// <summary>Tells whether a job is still smelting or ready.</summary>
    /// <param name="startedAtMs">Server time the job started, Unix milliseconds.</param>
    /// <param name="nowMs">Server time now, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <returns><see cref="FurnaceState.Working"/> or <see cref="FurnaceState.Ready"/>.</returns>
    public static FurnaceState StateOf(long startedAtMs, long nowMs, GameCalendar calendar)
        => nowMs >= ReadyAtMs(startedAtMs, calendar) ? FurnaceState.Ready : FurnaceState.Working;
}
