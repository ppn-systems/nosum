// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Nodes;

/// <summary>One thing that can be foraged: the item a pick-up gives and the seasons it turns up in.</summary>
/// <param name="ItemId">The item picked up, from <see cref="ItemCatalog"/>.</param>
/// <param name="Seasons">The seasons it appears in.</param>
/// <param name="Pool">Which spots it turns up on: <see cref="ForagePools.Woodland"/> for the glyphs of the <c>Paths</c> layer, <see cref="ForagePools.Shore"/> for the beach's sand (docs/adr/ADR-037 D3).</param>
public readonly record struct ForageEntry(int ItemId, SeasonSet Seasons, byte Pool = ForagePools.Woodland);

/// <summary>The forage pools. A forage spot (<see cref="NodeSite"/> of kind <see cref="NodeKind.Forage"/>) carries its pool in <see cref="NodeSite.Variant"/>.</summary>
public static class ForagePools
{
    /// <summary>Plants of fields and woods: every spot the <c>Paths</c> layer marks.</summary>
    public const byte Woodland = 0;

    /// <summary>Shells and coral that wash up on the beach.</summary>
    public const byte Shore = 1;
}

/// <summary>
/// The forage of the world (docs/adr/ADR-025 D4), as data. A forage node (<see cref="NodeKind.Forage"/>) stores the index of its entry here in
/// <see cref="ResourceNode.Variant"/>, so the list is append-only like an id. Spring: spring onion, leek, dandelion, daffodil. Summer: grape,
/// spice berry, sweet pea. Fall: blackberry, hazelnut, wild plum, mushroom. Winter: winter root, crystal fruit, snow yam. The beach's shore pool (docs/adr/ADR-037 D3)
/// is appended after them: clam, cockle, coral, sea urchin, nautilus shell, rainbow shell.
/// </summary>
public static class ForageCatalog
{
    private static readonly ForageEntry[] s_all =
    [
        new(ItemCatalog.SpringOnion, SeasonSet.Spring),
        new(ItemCatalog.Leek, SeasonSet.Spring),
        new(ItemCatalog.Dandelion, SeasonSet.Spring),
        new(ItemCatalog.Daffodil, SeasonSet.Spring),
        new(ItemCatalog.Grape, SeasonSet.Summer),
        new(ItemCatalog.SpiceBerry, SeasonSet.Summer),
        new(ItemCatalog.SweetPea, SeasonSet.Summer),
        new(ItemCatalog.Blackberry, SeasonSet.Fall),
        new(ItemCatalog.Hazelnut, SeasonSet.Fall),
        new(ItemCatalog.WildPlum, SeasonSet.Fall),
        new(ItemCatalog.Mushroom, SeasonSet.Fall),
        new(ItemCatalog.WinterRoot, SeasonSet.Winter),
        new(ItemCatalog.CrystalFruit, SeasonSet.Winter),
        new(ItemCatalog.SnowYam, SeasonSet.Winter),
        new(ItemCatalog.Clam, SeasonSet.All, ForagePools.Shore),
        new(ItemCatalog.Cockle, SeasonSet.All, ForagePools.Shore),
        new(ItemCatalog.Coral, SeasonSet.Spring | SeasonSet.Summer | SeasonSet.Fall, ForagePools.Shore),
        new(ItemCatalog.SeaUrchin, SeasonSet.Summer | SeasonSet.Fall, ForagePools.Shore),
        new(ItemCatalog.NautilusShell, SeasonSet.Fall | SeasonSet.Winter, ForagePools.Shore),
        new(ItemCatalog.RainbowShell, SeasonSet.Summer, ForagePools.Shore),
    ];

    /// <summary>Gets every forage entry; the position in the list is the node's variant.</summary>
    public static IReadOnlyList<ForageEntry> All => s_all;

    /// <summary>Looks up an entry by a node's variant.</summary>
    /// <param name="variant">The variant stored in a forage node.</param>
    /// <param name="entry">The entry when the variant names one.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public static bool TryGet(byte variant, out ForageEntry entry)
    {
        if (variant < s_all.Length)
        {
            entry = s_all[variant];
            return true;
        }

        entry = default;
        return false;
    }

    /// <summary>Returns the season a node day falls in. Node days count from the calendar's epoch, which starts a spring (<see cref="GameCalendar"/>).</summary>
    /// <param name="nodeDay">The node day, from 0.</param>
    /// <returns>The season.</returns>
    public static Season SeasonOfDay(long nodeDay)
        => (Season)(int)(Math.Max(0, nodeDay) / GameCalendar.DaysPerSeason % GameCalendar.SeasonsPerYear);

    /// <summary>Returns whether a forage variant turns up in a season.</summary>
    /// <param name="variant">The variant.</param>
    /// <param name="season">The season.</param>
    /// <returns><see langword="true"/> when the variant is known and belongs to the season.</returns>
    public static bool IsInSeason(byte variant, Season season) => TryGet(variant, out ForageEntry entry) && entry.Seasons.Contains(season);

    /// <summary>Picks a random variant that turns up in a season.</summary>
    /// <param name="season">The season.</param>
    /// <param name="pool">The pool of the spot, <see cref="NodeSite.Variant"/> of a forage site.</param>
    /// <param name="random">The deterministic roller.</param>
    /// <returns>A variant, or <see langword="null"/> when nothing of that pool turns up in the season.</returns>
    public static byte? Roll(Season season, byte pool, ref NodeRandom random)
    {
        int count = 0;
        for (int i = 0; i < s_all.Length; i++)
        {
            count += s_all[i].Pool == pool && s_all[i].Seasons.Contains(season) ? 1 : 0;
        }

        if (count == 0)
        {
            return null;
        }

        int pick = random.Next(count);
        for (int i = 0; i < s_all.Length; i++)
        {
            if (s_all[i].Pool == pool && s_all[i].Seasons.Contains(season) && pick-- == 0)
            {
                return (byte)i;
            }
        }

        return null;
    }
}
