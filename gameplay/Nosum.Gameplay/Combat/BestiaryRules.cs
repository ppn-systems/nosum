// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Nosum.Gameplay.Combat;

/// <summary>
/// The hunting log's milestones and slayer titles (docs/adr/ADR-068). A player's kills of a species are counted on the server; each milestone is reached
/// at a kill count, and reaching the last one earns that species' slayer title, which the player may equip.
/// </summary>
public static class BestiaryRules
{
    private static readonly int[] s_milestones = [10, 50, 100];

    /// <summary>Gets the kill counts of the milestones, lowest first. The last one earns the slayer title.</summary>
    public static IReadOnlyList<int> Milestones => s_milestones;

    /// <summary>Counts the milestones a kill count has reached.</summary>
    /// <param name="kills">How many monsters of one species were slain.</param>
    /// <returns>Zero to three.</returns>
    public static int TierOf(int kills)
    {
        int tier = 0;
        foreach (int milestone in s_milestones)
        {
            if (kills < milestone)
            {
                break;
            }

            tier++;
        }

        return tier;
    }

    /// <summary>Gets the next kill count to reach, or zero when every milestone is reached.</summary>
    /// <param name="kills">How many monsters of one species were slain.</param>
    /// <returns>The next milestone's kill count, or <c>0</c>.</returns>
    public static int NextMilestone(int kills)
    {
        int tier = TierOf(kills);
        return tier < s_milestones.Length ? s_milestones[tier] : 0;
    }

    /// <summary>Tells whether a kill count earns the species' slayer title, which means the last milestone is reached.</summary>
    /// <param name="kills">How many monsters of one species were slain.</param>
    /// <returns><see langword="true"/> when the title is earned.</returns>
    public static bool EarnsTitle(int kills) => kills >= s_milestones[^1];

    /// <summary>Tells whether a player may equip a species' title: the title is earned, or the player takes the title off (<see cref="MonsterKind.None"/>).</summary>
    /// <param name="species">The species of the title; <see cref="MonsterKind.None"/> takes it off.</param>
    /// <param name="kills">The player's kill count of that species.</param>
    /// <returns><see langword="true"/> when the request may be applied.</returns>
    public static bool MayEquip(MonsterKind species, int kills) => species == MonsterKind.None || EarnsTitle(kills);
}
