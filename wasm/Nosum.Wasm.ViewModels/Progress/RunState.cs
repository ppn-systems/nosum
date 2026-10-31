// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Progress;

/// <summary>
/// The running state the HUD shows (docs/adr/ADR-085 D3, D6): the energy bar drains and glows while the farmer runs, and a toast says why the run
/// stopped when the energy ran out. The map scene reports a change only when it happens, never per frame.
/// </summary>
public enum RunState
{
    /// <summary>Not running: walking, standing still, or run is not held.</summary>
    Stopped = 0,

    /// <summary>Running: run is held, the farmer walks and there is energy to spend.</summary>
    Running = 1,

    /// <summary>Run is held and walking, but there is no energy left, so the farmer walks at the normal pace.</summary>
    OutOfEnergy = 2,
}

/// <summary>The rule that turns the held keys and the energy into a <see cref="RunState"/>, pure so it can be tested (docs/adr/ADR-085 D3).</summary>
public static class RunStateRules
{
    /// <summary>Resolves the running state of one frame.</summary>
    /// <param name="walking">Whether a direction is held and the farmer is not in a swing.</param>
    /// <param name="runHeld">Whether run is held.</param>
    /// <param name="energyKnown">Whether the server has said how much energy the player has.</param>
    /// <param name="energy">The player's energy, when known.</param>
    /// <returns>Running with energy, out of energy while run is held and walking with none, otherwise stopped.</returns>
    public static RunState Resolve(bool walking, bool runHeld, bool energyKnown, int energy)
    {
        if (!walking || !runHeld || !energyKnown)
        {
            return RunState.Stopped;
        }

        return energy > 0 ? RunState.Running : RunState.OutOfEnergy;
    }
}
