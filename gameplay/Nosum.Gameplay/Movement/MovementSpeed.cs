// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Movement;

/// <summary>
/// The one movement speed multiplier a player walks at (docs/adr/ADR-081 D3): footwear × speed buff × running, clamped to
/// <see cref="MaxFactor"/>. The server computes it for the move validator and the client walker computes the same value from
/// the same catalogue, so a boosted claim is accepted only as far as the cap allows. Pure: nothing is read from a clock.
/// </summary>
public static class MovementSpeed
{
    /// <summary>
    /// The hard cap on the combined multiplier: no stack of footwear, buff and running walks faster than 50% over the base speed.
    /// Winged boots with Swiftness reach it exactly.
    /// </summary>
    public const double MaxFactor = 1.5;

    /// <summary>Returns the multiplier the footwear item gives; 1 for no footwear or an item without a speed bonus.</summary>
    /// <param name="feetItemId">The worn item id on the feet, or zero.</param>
    /// <returns>At least 1.</returns>
    public static double FootwearFactor(int feetItemId)
        => ItemCatalog.TryGet(feetItemId, out ItemDefinition? boots) && boots.Wear == WearSlot.Feet
            ? 1 + (boots.MoveSpeedPercent / 100.0)
            : 1;

    /// <summary>
    /// Combines the multipliers into the one the validator and the walker use: the product, clamped to [1, <see cref="MaxFactor"/>].
    /// </summary>
    /// <param name="footwearFactor">From <see cref="FootwearFactor"/>; 1 for none.</param>
    /// <param name="buffFactor">From <see cref="StatusEffectRules.MoveSpeedFactor"/>; 1 for none.</param>
    /// <param name="runFactor">Running multiplier (docs/adr/ADR-081 D6); 1 until running is in effect.</param>
    /// <returns>The combined multiplier, in [1, <see cref="MaxFactor"/>].</returns>
    public static double Combine(double footwearFactor, double buffFactor, double runFactor = 1)
        => Math.Clamp(footwearFactor * buffFactor * runFactor, 1, MaxFactor);

    /// <summary>Returns the combined multiplier of a worn footwear item and a buff in the buff slot.</summary>
    /// <param name="feetItemId">The worn item id on the feet, or zero.</param>
    /// <param name="buff">The buff in the buff slot, or <see cref="StatusEffectKind.None"/>.</param>
    /// <param name="runFactor">Running multiplier; 1 until running is in effect.</param>
    /// <returns>The combined multiplier, in [1, <see cref="MaxFactor"/>].</returns>
    public static double For(int feetItemId, StatusEffectKind buff, double runFactor = 1)
        => Combine(FootwearFactor(feetItemId), StatusEffectRules.MoveSpeedFactor(buff), runFactor);
}
