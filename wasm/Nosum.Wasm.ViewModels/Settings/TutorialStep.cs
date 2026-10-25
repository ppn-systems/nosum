// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.ViewModels.Settings;

/// <summary>A step of the first-run hints (docs/adr/ADR-033 D3), in the order they are shown.</summary>
public enum TutorialStep
{
    /// <summary>Walk around.</summary>
    Move = 0,

    /// <summary>Use the held tool.</summary>
    Use = 1,

    /// <summary>Sell something at the shipping bin.</summary>
    Sell = 2,

    /// <summary>Open a shop.</summary>
    Shop = 3,

    /// <summary>Visit Town.</summary>
    Town = 4,
}

/// <summary>Helpers for the bit set of finished <see cref="TutorialStep"/>s.</summary>
public static class TutorialSteps
{
    /// <summary>Gets the mask with every step set.</summary>
    public static int AllMask { get; } = (1 << Enum.GetValues<TutorialStep>().Length) - 1;

    /// <summary>Gets the bit of a step.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The bit.</returns>
    public static int Bit(TutorialStep step) => 1 << (int)step;
}
