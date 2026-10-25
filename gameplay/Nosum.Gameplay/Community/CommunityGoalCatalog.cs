// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Community;

/// <summary>
/// One server-wide goal of a season (docs/adr/ADR-030 D1): everybody delivers <see cref="Target"/> of <see cref="ItemId"/>; when the total
/// is reached the goal is complete for good and pays its reward. The numbers are product defaults, not balance.
/// </summary>
/// <param name="Id">Stable numeric id, greater than zero; the only goal name that crosses the wire. Append-only.</param>
/// <param name="Key">Stable key, localization entries <c>goal.&lt;key&gt;.name</c> and <c>goal.&lt;key&gt;.reward</c>.</param>
/// <param name="Season">The season the goal is open in.</param>
/// <param name="ItemId">The item that is delivered.</param>
/// <param name="Target">How many items complete the goal.</param>
/// <param name="UnlockKey">The unlock flag the goal sets (<see cref="CommunityUnlockKeys"/>), or an empty string when it unlocks nothing.</param>
/// <param name="CoinPool">Coins shared among the contributors in proportion to what each delivered; zero for none.</param>
/// <param name="CoinCapPerPlayer">The most coins one contributor can receive from the pool.</param>
public sealed record CommunityGoalDefinition(int Id, string Key, Season Season, int ItemId, long Target, string UnlockKey, long CoinPool, long CoinCapPerPlayer);

/// <summary>
/// The goals of every season, as data. Each season has one goal per entry here; a goal that was completed stays completed for the server's
/// lifetime, one that ran out of time (the season ended first) opens again the next time its season comes round. More crops come with
/// the crop roster (docs/adr/ADR-025): a new goal is one more entry.
/// </summary>
public static class CommunityGoalCatalog
{
    /// <summary>Spring: parsnips for the bridge to the Beach.</summary>
    public const int SpringBridge = 1;

    /// <summary>Summer: cauliflowers for a festival stall.</summary>
    public const int SummerFestival = 2;

    /// <summary>Fall: radishes for the harvest stall.</summary>
    public const int FallHarvest = 3;

    /// <summary>Winter: parsnips for the lanterns of the town square.</summary>
    public const int WinterLanterns = 4;

    /// <summary>The id of the Town board counter on the map (<c>ShopPoint.ShopId</c>); not a shop, so it is not in <c>ShopCatalog</c>. Ids from 1000 name counters that are not shops.</summary>
    public const int BoardCounterId = 1000;

    private static readonly CommunityGoalDefinition[] s_all =
    [
        new(SpringBridge, "spring_bridge", Season.Spring, ItemCatalog.Parsnip, 5_000, CommunityUnlockKeys.BeachBridge, 50_000, 5_000),
        new(SummerFestival, "summer_festival", Season.Summer, ItemCatalog.Cauliflower, 1_500, CommunityUnlockKeys.FestivalStall, 45_000, 5_000),
        new(FallHarvest, "fall_harvest", Season.Fall, ItemCatalog.Radish, 2_500, CommunityUnlockKeys.HarvestStall, 40_000, 4_000),
        new(WinterLanterns, "winter_lanterns", Season.Winter, ItemCatalog.Parsnip, 1_000, CommunityUnlockKeys.TownLanterns, 10_000, 1_000),
    ];

    /// <summary>Gets every goal.</summary>
    public static IReadOnlyList<CommunityGoalDefinition> All => s_all;

    /// <summary>Looks up a goal by its id.</summary>
    /// <param name="goalId">Goal id as sent by a client; not trusted.</param>
    /// <param name="goal">The goal when found.</param>
    /// <returns><see langword="true"/> when the id names a goal.</returns>
    public static bool TryGet(int goalId, [NotNullWhen(true)] out CommunityGoalDefinition? goal)
    {
        foreach (CommunityGoalDefinition candidate in s_all)
        {
            if (candidate.Id == goalId)
            {
                goal = candidate;
                return true;
            }
        }

        goal = null;
        return false;
    }

    /// <summary>Lists the goals that are open in a season.</summary>
    /// <param name="season">The season.</param>
    /// <returns>The goals, in catalogue order.</returns>
    public static CommunityGoalDefinition[] ForSeason(Season season)
    {
        List<CommunityGoalDefinition> goals = [];
        foreach (CommunityGoalDefinition goal in s_all)
        {
            if (goal.Season == season)
            {
                goals.Add(goal);
            }
        }

        return [.. goals];
    }
}

/// <summary>
/// The unlock flags a completed goal sets. Other features ask the server whether a flag is set (<c>ICommunityUnlocks.IsUnlocked</c>) and
/// open their content accordingly; the keys are stable strings stored in the database. The first target is the Beach map (docs/adr/ADR-030).
/// </summary>
public static class CommunityUnlockKeys
{
    /// <summary>The bridge to the Beach is built.</summary>
    public const string BeachBridge = "beach_bridge";

    /// <summary>The summer festival stall sells a rare seed.</summary>
    public const string FestivalStall = "festival_stall";

    /// <summary>The harvest stall opens in the town.</summary>
    public const string HarvestStall = "harvest_stall";

    /// <summary>The town square is lit for the winter.</summary>
    public const string TownLanterns = "town_lanterns";
}
