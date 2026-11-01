// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Wasm.ViewModels.Progress;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The HUD's running state (docs/adr/ADR-085 D3): the energy bar drains while running, and the toast says why a run stopped.</summary>
public sealed class RunStateRulesTests
{
    [Fact]
    public void Resolve_WalkingWithRunHeldAndEnergy_IsRunning()
    {
        RunStateRules.Resolve(walking: true, runHeld: true, energyKnown: true, energy: 10).Should().Be(RunState.Running);
    }

    [Fact]
    public void Resolve_WalkingWithRunHeldAndNoEnergy_IsOutOfEnergy()
    {
        // The toast fires once, on this transition: run is held, the farmer walks, and there is nothing left to spend.
        RunStateRules.Resolve(walking: true, runHeld: true, energyKnown: true, energy: 0).Should().Be(RunState.OutOfEnergy);
    }

    [Theory]
    [InlineData(false, true, true, 10)]
    [InlineData(true, false, true, 10)]
    [InlineData(true, true, false, 0)]
    public void Resolve_NotWalkingOrNotHeldOrEnergyUnknown_IsStopped(bool walking, bool runHeld, bool energyKnown, int energy)
    {
        RunStateRules.Resolve(walking, runHeld, energyKnown, energy).Should().Be(RunState.Stopped);
    }
}
