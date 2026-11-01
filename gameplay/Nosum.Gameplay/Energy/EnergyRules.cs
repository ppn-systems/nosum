// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Energy;

/// <summary>
/// The daily energy budget (docs/adr/ADR-023 D1, D2): what a tool use costs, when energy refills and what eating restores.
/// Pure: the game day and the skill level are parameters.
/// </summary>
public static class EnergyRules
{
    /// <summary>Largest energy at level zero.</summary>
    public const int MaxEnergy = 270;

    /// <summary>Skill levels per point of discount.</summary>
    public const int LevelsPerDiscount = 4;

    /// <summary>The refill marker of a player whose energy was never refilled: any game day is later, so the first read fills it.</summary>
    public const long NeverRefilled = -1;

    /// <summary>Gets what one successful use of a tool costs before any discount.</summary>
    /// <param name="tool">The tool.</param>
    /// <returns>Energy; zero for the scythe and for non-tools.</returns>
    public static int BaseCost(ToolKind tool) => tool switch
    {
        ToolKind.Hoe or ToolKind.WateringCan or ToolKind.Axe or ToolKind.Pickaxe => 3,
        _ => 0,
    };

    /// <summary>Gets what one successful use of a tool costs a player with a skill level: one less per four levels, never below one (the scythe stays free).</summary>
    /// <param name="tool">The tool.</param>
    /// <param name="skillLevel">The level of the skill the tool belongs to.</param>
    /// <returns>Energy to spend.</returns>
    public static int Cost(ToolKind tool, int skillLevel)
    {
        int baseCost = BaseCost(tool);
        return baseCost == 0 ? 0 : System.Math.Max(1, baseCost - (System.Math.Max(0, skillLevel) / LevelsPerDiscount));
    }

    /// <summary>Gets what one successful use of a tool of a given <paramref name="tier"/> costs a player with a skill level.</summary>
    /// <param name="tool">The tool.</param>
    /// <param name="skillLevel">The level of the skill the tool belongs to.</param>
    /// <param name="tier">The tool tier; upgraded tiers grant an additional efficiency discount down to the minimum of one energy.</param>
    /// <returns>Energy to spend.</returns>
    public static int Cost(ToolKind tool, int skillLevel, ToolTier tier)
    {
        int baseCost = BaseCost(tool);
        if (baseCost == 0)
        {
            return 0;
        }

        int tierDiscount = tier >= ToolTier.Copper ? 1 : 0;
        return System.Math.Max(1, baseCost - tierDiscount - (System.Math.Max(0, skillLevel) / LevelsPerDiscount));
    }

    /// <summary>Applies the daily refill: energy is full again once a game day has passed since the last refill.</summary>
    /// <param name="energy">The stored energy.</param>
    /// <param name="refilledDay">The game day of the last refill, or <see cref="NeverRefilled"/>.</param>
    /// <param name="today">The current game day.</param>
    /// <returns>The energy now and the day it counts as refilled on.</returns>
    public static (int Energy, long RefilledDay) Refill(int energy, long refilledDay, long today)
        => today > refilledDay ? (MaxEnergy, today) : (energy, refilledDay);

    /// <summary>Spends energy.</summary>
    /// <param name="energy">The energy now.</param>
    /// <param name="cost">What to spend.</param>
    /// <param name="left">The energy afterwards when the spend is allowed.</param>
    /// <returns><see langword="false"/> when the energy is zero (the use is refused; no fainting) or too low for the cost.</returns>
    public static bool TrySpend(int energy, int cost, out int left)
    {
        if (cost > 0 && energy < cost)
        {
            left = energy;
            return false;
        }

        left = energy - cost;
        return true;
    }

    /// <summary>Adds eaten energy, capped at the maximum.</summary>
    /// <param name="energy">The energy now.</param>
    /// <param name="restores">What the food restores.</param>
    /// <returns>The energy afterwards.</returns>
    public static int Eat(int energy, int restores) => System.Math.Min(MaxEnergy, energy + restores);
}
