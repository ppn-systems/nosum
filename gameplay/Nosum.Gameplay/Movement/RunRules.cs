// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Movement;

/// <summary>
/// Running (docs/adr/ADR-085): a held run key walks faster and costs a little energy for every second of running. The authority applies the
/// same speed multiplier as the client walker, so <see cref="MoveValidator"/> accepts a run exactly as far as the client can take it. Pure: time and
/// energy are parameters.
/// </summary>
public static class RunRules
{
    /// <summary>Speed multiplier while running, on top of any speed buff (docs/adr/ADR-085 D1).</summary>
    public const double SpeedFactor = 1.5;

    /// <summary>Energy spent per second of running: one energy every four seconds (docs/adr/ADR-085 D2).</summary>
    public const double EnergyPerSecond = 0.25;

    /// <summary>
    /// Most seconds one call charges. A player who stood still and then runs has been idle since the last accepted move; that idle time is not running,
    /// so each call charges no more than this (the validator's own budget window, <see cref="MoveValidator.MaxBudgetSeconds"/>).
    /// </summary>
    public const double MaxChargedSeconds = MoveValidator.MaxBudgetSeconds;

    /// <summary>Returns the speed multiplier for a move: the footwear and buff multiplier times the run factor while running, clamped to <see cref="MovementSpeed.MaxFactor"/> (docs/adr/ADR-084 D3).</summary>
    /// <param name="buffFactor">The speed multiplier of the active speed buffs (1 for none).</param>
    /// <param name="running">Whether the player runs.</param>
    /// <returns>The multiplier, at least 1.</returns>
    public static double SpeedMultiplier(double buffFactor, bool running) => MovementSpeed.Combine(buffFactor, 1, running ? SpeedFactor : 1);

    /// <summary>
    /// Adds the energy owed for running over <paramref name="seconds"/> to <paramref name="debt"/> and takes the whole energy units out of it.
    /// Fractions carry over, so a long run spends exactly <see cref="EnergyPerSecond"/> per second however the time is split.
    /// </summary>
    /// <param name="debt">Energy owed and not yet spent, below one unit between calls; updated.</param>
    /// <param name="seconds">Seconds since the last call; at most <see cref="MaxChargedSeconds"/> of them count as running.</param>
    /// <returns>Whole energy units to spend now.</returns>
    public static int TakeEnergy(ref double debt, double seconds)
    {
        debt += Math.Clamp(seconds, 0, MaxChargedSeconds) * EnergyPerSecond;
        int whole = (int)Math.Floor(debt);
        debt -= whole;
        return whole;
    }
}
