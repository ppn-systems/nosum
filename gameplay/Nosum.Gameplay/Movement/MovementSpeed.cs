// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Movement;

/// <summary>
/// The one movement speed multiplier a player walks at (docs/adr/ADR-084 D3): footwear × speed buff × running, clamped to
/// <see cref="MaxFactor"/>. The server computes it for the move validator and the client walker computes the same value from
/// the same catalogue, so a boosted claim is accepted only as far as the cap allows. Pure: nothing is read from a clock.
/// </summary>
public static class MovementSpeed
{
    /// <summary>
    /// The hard cap on the combined multiplier (docs/adr/ADR-084 amendment, docs/adr/ADR-085 D1): no stack of footwear, buff and running walks
    /// faster than 80% over the base speed. Winged boots while running (1.20 × 1.5) reach it exactly; walking never reaches it (the highest
    /// walking stack is winged boots with Swiftness, 1.50).
    /// </summary>
    public const double MaxFactor = 1.8;

    /// <summary>Returns the multiplier the footwear item gives; 1 for no footwear or an item without a speed bonus.</summary>
    /// <param name="feetItemId">The worn item id on the feet, or zero.</param>
    /// <returns>At least 1.</returns>
    public static double FootwearFactor(int feetItemId)
        => ItemCatalog.TryGet(feetItemId, out ItemDefinition? boots) && boots.Wear == WearSlot.Feet
            ? 1 + (boots.MoveSpeedPercent / 100.0)
            : 1;

    /// <summary>
    /// Returns the footwear × speed buff product, not clamped. The move path passes it to <see cref="Movement.RunRules.SpeedMultiplier"/>, which
    /// adds the run factor and clamps once; clamping here as well would cap the boots before running applies (the bug of ADR-084 amendment 2).
    /// </summary>
    /// <param name="feetItemId">The worn item id on the feet, or zero.</param>
    /// <param name="buff">The buff in the buff slot, or <see cref="StatusEffectKind.None"/>.</param>
    /// <returns>At least 1, and not capped.</returns>
    public static double Product(int feetItemId, StatusEffectKind buff) => FootwearFactor(feetItemId) * StatusEffectRules.MoveSpeedFactor(buff);

    /// <summary>
    /// Combines the multipliers into the one the validator and the walker use: the product, clamped to [1, <see cref="MaxFactor"/>].
    /// </summary>
    /// <param name="footwearFactor">From <see cref="FootwearFactor"/>; 1 for none.</param>
    /// <param name="buffFactor">From <see cref="StatusEffectRules.MoveSpeedFactor"/>; 1 for none.</param>
    /// <param name="runFactor">Running multiplier (docs/adr/ADR-084 D6); 1 until running is in effect.</param>
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
