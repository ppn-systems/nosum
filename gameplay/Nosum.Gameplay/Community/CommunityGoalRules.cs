// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Economy;

namespace Nosum.Gameplay.Community;

/// <summary>Why a delivery was refused before any item moved.</summary>
public enum DeliverVerdict
{
    /// <summary>The delivery is allowed.</summary>
    Ok = 0,

    /// <summary>The goal id is not in the catalogue.</summary>
    UnknownGoal = 1,

    /// <summary>The quantity is zero, negative or larger than one delivery may hold.</summary>
    InvalidQuantity = 2,
}

/// <summary>Pure rules of the community goals (docs/adr/ADR-030): validating a delivery, sharing the reward and the consolation payout.</summary>
public static class CommunityGoalRules
{
    /// <summary>The most items one delivery may carry.</summary>
    public const int MaxDeliveryPerRequest = 3_000;

    /// <summary>Checks a delivery against the catalogue.</summary>
    /// <param name="goalId">The goal.</param>
    /// <param name="quantity">How many items.</param>
    /// <returns><see cref="DeliverVerdict.Ok"/> or the reason.</returns>
    public static DeliverVerdict Validate(int goalId, int quantity)
    {
        if (!CommunityGoalCatalog.TryGet(goalId, out _))
        {
            return DeliverVerdict.UnknownGoal;
        }

        return quantity < 1 || quantity > MaxDeliveryPerRequest ? DeliverVerdict.InvalidQuantity : DeliverVerdict.Ok;
    }

    /// <summary>Tells how many more items a goal takes.</summary>
    /// <param name="goal">The goal.</param>
    /// <param name="total">How many were delivered so far.</param>
    /// <returns>The remaining count, never negative.</returns>
    public static long Remaining(CommunityGoalDefinition goal, long total)
    {
        ArgumentNullException.ThrowIfNull(goal);

        return Math.Max(0, goal.Target - total);
    }

    /// <summary>Shares the coin pool: a contributor gets their proportion of what was delivered, rounded down, and never more than the cap.</summary>
    /// <param name="goal">The goal that was completed.</param>
    /// <param name="delivered">What this contributor delivered.</param>
    /// <param name="total">What everybody delivered; the sum of every contributor's share of the goal.</param>
    /// <returns>The coins this contributor receives; zero when the goal has no pool or nothing was delivered.</returns>
    public static long RewardShare(CommunityGoalDefinition goal, long delivered, long total)
    {
        ArgumentNullException.ThrowIfNull(goal);

        if (goal.CoinPool <= 0 || delivered <= 0 || total <= 0)
        {
            return 0;
        }

        // Pool and counts are small (millions at most), so the product fits a long; the cap bounds one player's take.
        long share = goal.CoinPool * Math.Min(delivered, total) / total;
        return Math.Min(share, goal.CoinCapPerPlayer);
    }

    /// <summary>The consolation for a goal that ran out of time: half the shipping-bin price of every item the contributor delivered, rounded down.</summary>
    /// <param name="goal">The goal that expired.</param>
    /// <param name="delivered">What this contributor delivered.</param>
    /// <returns>The coins this contributor receives.</returns>
    public static long Consolation(CommunityGoalDefinition goal, long delivered)
    {
        ArgumentNullException.ThrowIfNull(goal);

        if (delivered <= 0 || !ShippingPrices.TryGet(goal.ItemId, out int each))
        {
            return 0;
        }

        return delivered * each / 2;
    }
}
