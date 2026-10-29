// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Coops;

/// <summary>
/// One shared objective of co-ops (docs/adr/ADR-067 D1): the members' harvests of <see cref="ItemId"/> add up to <see cref="Target"/>, and the co-op keeps the
/// trophy when it gets there. The numbers are product defaults, not balance.
/// </summary>
/// <param name="Id">Stable numeric id, greater than zero; the only objective name that crosses the wire. Append-only.</param>
/// <param name="Key">Stable key, localization entry <c>coop_objective.&lt;key&gt;</c> for the title and <c>coop_trophy.&lt;key&gt;</c> for the trophy line.</param>
/// <param name="ItemId">The crop whose harvests count.</param>
/// <param name="Target">How many harvested items complete the objective.</param>
public sealed record CoopObjectiveDefinition(int Id, string Key, int ItemId, int Target);

/// <summary>The shared objectives of co-ops, as data (docs/adr/ADR-067 D1). A new objective is one more entry with a new id.</summary>
public static class CoopObjectiveCatalog
{
    /// <summary>Parsnips for the co-op pantry.</summary>
    public const int ParsnipPantry = 1;

    /// <summary>Radishes for the co-op pantry.</summary>
    public const int RadishPantry = 2;

    /// <summary>Strawberries for the co-op orchard fence.</summary>
    public const int StrawberryOrchard = 3;

    private static readonly CoopObjectiveDefinition[] s_all =
    [
        new(ParsnipPantry, "parsnip_pantry", ItemCatalog.Parsnip, 300),
        new(RadishPantry, "radish_pantry", ItemCatalog.Radish, 200),
        new(StrawberryOrchard, "strawberry_orchard", ItemCatalog.Strawberry, 500),
    ];

    /// <summary>Gets every objective, in catalogue order.</summary>
    public static IReadOnlyList<CoopObjectiveDefinition> All => s_all;

    /// <summary>Looks up an objective by its id.</summary>
    /// <param name="objectiveId">Objective id as stored or sent; not trusted.</param>
    /// <param name="objective">The objective when found.</param>
    /// <returns><see langword="true"/> when the id names an objective.</returns>
    public static bool TryGet(int objectiveId, [NotNullWhen(true)] out CoopObjectiveDefinition? objective)
    {
        foreach (CoopObjectiveDefinition candidate in s_all)
        {
            if (candidate.Id == objectiveId)
            {
                objective = candidate;
                return true;
            }
        }

        objective = null;
        return false;
    }

    /// <summary>Finds the objective a harvested item counts toward.</summary>
    /// <param name="itemId">The harvested item.</param>
    /// <param name="objective">The objective when one counts this item.</param>
    /// <returns><see langword="true"/> when an objective counts the item.</returns>
    public static bool TryForItem(int itemId, [NotNullWhen(true)] out CoopObjectiveDefinition? objective)
    {
        foreach (CoopObjectiveDefinition candidate in s_all)
        {
            if (candidate.ItemId == itemId)
            {
                objective = candidate;
                return true;
            }
        }

        objective = null;
        return false;
    }
}

/// <summary>The pure rules of shared co-op objectives (docs/adr/ADR-067): how a harvest adds to the ledger and when the trophy is earned.</summary>
public static class CoopObjectiveRules
{
    /// <summary>Adds a harvest to the ledger total, saturating at <see cref="int.MaxValue"/> so a long-running co-op never wraps around.</summary>
    /// <param name="contributed">The total so far.</param>
    /// <param name="quantity">How many were harvested; at least one.</param>
    /// <returns>The new total.</returns>
    public static int Credit(int contributed, int quantity)
    {
        long sum = (long)contributed + quantity;
        return sum > int.MaxValue ? int.MaxValue : (int)sum;
    }

    /// <summary>Whether the total has reached the target.</summary>
    /// <param name="contributed">The total.</param>
    /// <param name="target">The objective's target.</param>
    /// <returns><see langword="true"/> when the objective is complete.</returns>
    public static bool IsComplete(int contributed, int target) => contributed >= target;

    /// <summary>
    /// Whether a harvest earns the trophy now: the objective is reached by this harvest and was not reached before. The trophy is kept once, however many harvests
    /// follow.
    /// </summary>
    /// <param name="before">The total before the harvest.</param>
    /// <param name="after">The total after the harvest.</param>
    /// <param name="target">The objective's target.</param>
    /// <param name="alreadyEarned">Whether the co-op already holds the trophy.</param>
    /// <returns><see langword="true"/> when the harvest completes the objective.</returns>
    public static bool EarnsTrophy(int before, int after, int target, bool alreadyEarned)
        => !alreadyEarned && !IsComplete(before, target) && IsComplete(after, target);
}
