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

    /// <summary>Energy spent per second of running, in thousandths of an energy: one energy every four seconds (docs/adr/ADR-085 D2).</summary>
    public const int EnergyMilliPerSecond = 250;

    /// <summary>Energy spent per second of running, in energy units (<see cref="EnergyMilliPerSecond"/> / 1000).</summary>
    public const double EnergyPerSecond = EnergyMilliPerSecond / 1000.0;

    /// <summary>
    /// Most seconds one call charges. A player who stood still and then runs has been idle since the last accepted move; that idle time is not running,
    /// so each call charges no more than this (the validator's own budget window, <see cref="MoveValidator.MaxBudgetSeconds"/>).
    /// </summary>
    public const double MaxChargedSeconds = MoveValidator.MaxBudgetSeconds;

    /// <summary>Energy in one unit, in thousandths; the debt counts in these.</summary>
    public const int MilliPerUnit = 1000;

    /// <summary>
    /// Returns the speed multiplier for a move while the farmer may run: the footwear and buff product (<see cref="MovementSpeed.Product"/>) times the
    /// run factor, clamped once to [1, <see cref="MovementSpeed.MaxFactor"/>] (docs/adr/ADR-084 D3, docs/adr/ADR-085 D1). The client walker and the
    /// move validator both call this, so the pace the client walks at is the pace the server accepts.
    /// </summary>
    /// <param name="speedProduct">The unclamped footwear × buff product; 1 for none.</param>
    /// <param name="running">Whether the player runs.</param>
    /// <returns>The multiplier, in [1, <see cref="MovementSpeed.MaxFactor"/>].</returns>
    public static double SpeedMultiplier(double speedProduct, bool running) => MovementSpeed.Combine(speedProduct, 1, running ? SpeedFactor : 1);

    /// <summary>
    /// Returns the energy a call owes for running over <paramref name="seconds"/>, before footwear, in thousandths of an energy. Idle time beyond
    /// <see cref="MaxChargedSeconds"/> is not running and costs nothing; the floor keeps whole milli-units only.
    /// </summary>
    /// <param name="seconds">Seconds since the last call.</param>
    /// <returns>Thousandths of an energy, zero or more.</returns>
    public static int BaseCostMilli(double seconds) => (int)Math.Floor(Math.Clamp(seconds, 0, MaxChargedSeconds) * EnergyMilliPerSecond);

    /// <summary>
    /// Adds <paramref name="costMilli"/> (the cost after footwear, <see cref="Movement.FootwearRules.RunEnergyCost"/>) to the debt and takes the
    /// whole energy units out of it. The debt stays below one unit between calls, so a long run spends exactly what its milli-costs add up to.
    /// </summary>
    /// <param name="debtMilli">Thousandths of an energy owed and not yet spent, below <see cref="MilliPerUnit"/> between calls; updated.</param>
    /// <param name="costMilli">Thousandths of an energy this call costs, zero or more.</param>
    /// <returns>Whole energy units to spend now.</returns>
    public static int TakeEnergy(ref int debtMilli, int costMilli)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(costMilli);
        debtMilli += costMilli;
        int whole = debtMilli / MilliPerUnit;
        debtMilli -= whole * MilliPerUnit;
        return whole;
    }
}
