// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Npcs;
using Nosum.Gameplay.Quests;
using Nosum.Gameplay.Weather;

namespace Nosum.Wasm.ViewModels.Npc;

/// <summary>The moment a merchant speaks in: the day, and the tags that are true now (docs/adr/ADR-080 D2).</summary>
/// <param name="DayNumber">The game day (<see cref="GameCalendar.DayNumberAt"/>), or zero while the calendar is not known.</param>
/// <param name="Tags">The season, weather, time of day and progress that hold now. Unknown parts are left out, so only untagged lines fit them.</param>
/// <param name="ActiveQuestKey">The key of the quest the farmer is working on, or <see langword="null"/> when none is (docs/adr/ADR-093 D3).</param>
public readonly record struct NpcLineContext(long DayNumber, NpcLineTags Tags, string? ActiveQuestKey = null)
{
    /// <summary>The mine floor a farmer must reach for the <see cref="NpcLineTags.DeepMiner"/> progress.</summary>
    public const int DeepMinerFloor = 10;

    /// <summary>Builds the moment from what the client knows.</summary>
    /// <param name="dayNumber">The game day, or zero while unknown.</param>
    /// <param name="season">The season, or <see langword="null"/> while the calendar is not known.</param>
    /// <param name="weather">The weather of the day, or <see langword="null"/> while the calendar is not known.</param>
    /// <param name="hour">The hour of the day, or <see langword="null"/> while the calendar is not known.</param>
    /// <param name="newcomer">Whether no quest is finished yet.</param>
    /// <param name="harvested">Whether the first harvest quest is finished.</param>
    /// <param name="deepMiner">Whether the farmer has reached <see cref="DeepMinerFloor"/>.</param>
    /// <param name="activeQuestKey">The key of the quest the farmer is working on, or <see langword="null"/> when none is.</param>
    /// <returns>The moment.</returns>
    public static NpcLineContext Of(long dayNumber, Season? season, WeatherKind? weather, int? hour, bool newcomer, bool harvested, bool deepMiner, string? activeQuestKey = null)
    {
        NpcLineTags tags = NpcLineTags.None;
        if (season is { } current)
        {
            tags |= SeasonTag(current);
        }

        if (weather is { } kind)
        {
            tags |= WeatherTag(kind);
        }

        if (hour is { } time)
        {
            tags |= TimeTag(time);
        }

        if (newcomer)
        {
            tags |= NpcLineTags.Newcomer;
        }

        if (harvested)
        {
            tags |= NpcLineTags.Harvested;
        }

        if (deepMiner)
        {
            tags |= NpcLineTags.DeepMiner;
        }

        return new NpcLineContext(dayNumber, tags, activeQuestKey);
    }

    /// <summary>Gets the tag of a season.</summary>
    /// <param name="season">The season.</param>
    /// <returns>The season's tag.</returns>
    public static NpcLineTags SeasonTag(Season season) => season switch
    {
        Season.Spring => NpcLineTags.Spring,
        Season.Summer => NpcLineTags.Summer,
        Season.Fall => NpcLineTags.Fall,
        _ => NpcLineTags.Winter,
    };

    /// <summary>Gets the tag of a day's weather: rain and storm share one.</summary>
    /// <param name="weather">The weather.</param>
    /// <returns>The weather's tag.</returns>
    public static NpcLineTags WeatherTag(WeatherKind weather) => weather switch
    {
        WeatherKind.Rain or WeatherKind.Storm => NpcLineTags.Wet,
        WeatherKind.Snow => NpcLineTags.Snow,
        _ => NpcLineTags.Sun,
    };

    /// <summary>Gets the tag of an hour: morning from five, day from eleven, evening from five in the afternoon, through the night.</summary>
    /// <param name="hour">The hour, zero to twenty-three.</param>
    /// <returns>The time-of-day tag.</returns>
    public static NpcLineTags TimeTag(int hour) => hour switch
    {
        >= 5 and < 11 => NpcLineTags.Morning,
        >= 11 and < 17 => NpcLineTags.Day,
        _ => NpcLineTags.Evening,
    };
}

/// <summary>
/// Chooses which line a merchant says (docs/adr/ADR-043 D7, issue #130, extended by docs/adr/ADR-080 D2): among the lines of the merchant's pool whose
/// moment is now, the one at the position of the day and of the conversation's line count. The same day, moment and count always give the same line;
/// the next line of a conversation is the next one that fits. A merchant's pool always has untagged lines, so there is always a line to say.
/// </summary>
public static class NpcGreeting
{
    /// <summary>Picks the key of the line to show.</summary>
    /// <param name="npc">The merchant.</param>
    /// <param name="context">The moment: the day and the tags that hold now.</param>
    /// <param name="slot">How many lines the conversation has already had: zero for the greeting, then one for each Talk.</param>
    /// <param name="exists">Whether a <c>Language.resx</c> key has text.</param>
    /// <returns>The key of the line, <c>Npc_{Id}_Line_{nn}</c>.</returns>
    public static string KeyFor(NpcDefinition npc, NpcLineContext context, int slot, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);

        // The greeting names the farmer's quest when this merchant is where the quest sends them (docs/adr/ADR-093 D3); the Talk lines go on as before.
        if (slot == 0 && context.ActiveQuestKey is { } questKey && QuestWaypoints.PlaceOf(questKey) == npc.Id)
        {
            string questLine = npc.TextKey("Quest_" + questKey);
            if (exists(questLine))
            {
                return questLine;
            }
        }

        List<NpcLine> fitting = [];
        foreach (NpcLine line in NpcLineCatalog.PoolOf(npc.Id))
        {
            if (Fits(line.Tags, context.Tags) && exists(LineKey(npc, line)))
            {
                fitting.Add(line);
            }
        }

        if (fitting.Count == 0)
        {
            return npc.TextKey("Line_01");
        }

        int start = (int)(Seed(npc.Id, context.DayNumber) % (uint)fitting.Count);
        return LineKey(npc, fitting[(start + slot) % fitting.Count]);
    }

    /// <summary>Tells whether a line fits a moment: in every group the line is tagged in, the moment has one of the line's values.</summary>
    /// <param name="line">The line's tags; none fits any moment.</param>
    /// <param name="now">The moment's tags.</param>
    /// <returns><see langword="true"/> when the line may be said now.</returns>
    public static bool Fits(NpcLineTags line, NpcLineTags now) => Group(line, now, NpcLineTags.SeasonGroup)
        && Group(line, now, NpcLineTags.WeatherGroup)
        && Group(line, now, NpcLineTags.TimeGroup)
        && Group(line, now, NpcLineTags.ProgressGroup);

    private static bool Group(NpcLineTags line, NpcLineTags now, NpcLineTags group)
    {
        NpcLineTags wanted = line & group;
        return wanted == NpcLineTags.None || (wanted & now) != NpcLineTags.None;
    }

    private static string LineKey(NpcDefinition npc, NpcLine line) => npc.TextKey("Line_" + line.Number.ToString("00", CultureInfo.InvariantCulture));

    // FNV-1a over the merchant's id and the day: stable across runs and platforms, unlike string.GetHashCode.
    private static uint Seed(string npcId, long day)
    {
        uint hash = 2166136261;
        foreach (char c in npcId)
        {
            hash = unchecked((hash ^ c) * 16777619);
        }

        for (int shift = 0; shift < 64; shift += 8)
        {
            hash = unchecked((hash ^ (uint)((day >> shift) & 0xFF)) * 16777619);
        }

        return hash;
    }
}
