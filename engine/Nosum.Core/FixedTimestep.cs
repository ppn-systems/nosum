// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Core;

/// <summary>
/// Fixed-timestep accumulator: turns variable frame times into a whole number of fixed
/// simulation steps plus an interpolation factor for rendering.
/// </summary>
/// <remarks>
/// The simulation always advances by <see cref="StepSeconds"/>, so game rules behave the same
/// at 30 FPS and 144 FPS. Rendering uses <see cref="Alpha"/> to interpolate between the last two
/// simulation states. Frame deltas are clamped to <see cref="MaxFrameSeconds"/> so a long pause
/// (tab in the background, debugger break) cannot trigger a burst of catch-up steps.
/// </remarks>
public struct FixedTimestep
{
    private double _accumulator;

    /// <summary>
    /// Initializes a new accumulator.
    /// </summary>
    /// <param name="stepSeconds">Length of one simulation step, in seconds.</param>
    /// <param name="maxFrameSeconds">Largest frame delta accepted per <see cref="Advance"/> call, in seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is not positive, or the frame cap is shorter than one step.</exception>
    public FixedTimestep(double stepSeconds, double maxFrameSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepSeconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameSeconds, stepSeconds);

        this.StepSeconds = stepSeconds;
        this.MaxFrameSeconds = maxFrameSeconds;
        _accumulator = 0;
    }

    /// <summary>Gets the length of one simulation step, in seconds.</summary>
    public readonly double StepSeconds { get; }

    /// <summary>Gets the largest frame delta accepted per call, in seconds.</summary>
    public readonly double MaxFrameSeconds { get; }

    /// <summary>
    /// Gets the interpolation factor in [0, 1) between the previous and the current simulation
    /// state, valid after <see cref="Advance"/>.
    /// </summary>
    public readonly double Alpha => _accumulator / this.StepSeconds;

    /// <summary>
    /// Adds one frame's elapsed time and returns how many fixed steps the simulation must run.
    /// </summary>
    /// <param name="frameSeconds">Elapsed wall time since the previous frame, in seconds. Negative values count as zero.</param>
    /// <returns>The number of fixed steps to run this frame.</returns>
    public int Advance(double frameSeconds)
    {
        _accumulator += Math.Clamp(frameSeconds, 0, this.MaxFrameSeconds);

        int steps = (int)(_accumulator / this.StepSeconds);
        _accumulator -= steps * this.StepSeconds;
        return steps;
    }
}
