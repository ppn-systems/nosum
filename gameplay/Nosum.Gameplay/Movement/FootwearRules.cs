// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Movement;

/// <summary>Pure rules of footwear that are not walking speed (docs/adr/ADR-084 D5).</summary>
public static class FootwearRules
{
    /// <summary>
    /// The running energy a run costs once the worn footwear saves its share (<see cref="ItemDefinition.RunEnergySavePercent"/>). Rounds down,
    /// never below zero, and returns the base cost for no footwear.
    /// </summary>
    /// <param name="baseCost">The energy a run costs without the footwear, zero or more.</param>
    /// <param name="feetItemId">The worn item id on the feet, or zero.</param>
    /// <returns>The energy the run costs.</returns>
    public static int RunEnergyCost(int baseCost, int feetItemId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseCost);
        if (!ItemCatalog.TryGet(feetItemId, out ItemDefinition? boots) || boots.Wear != WearSlot.Feet)
        {
            return baseCost;
        }

        int saved = (int)((long)baseCost * boots.RunEnergySavePercent / 100);
        return baseCost - saved;
    }
}
