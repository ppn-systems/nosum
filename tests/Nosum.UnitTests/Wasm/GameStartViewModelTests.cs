// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Nosum.Wasm.ViewModels.Maps;
using Nosum.Wasm.ViewModels.Startup;
using Xunit;
using static Nosum.UnitTests.Wasm.WasmTestSupport;

namespace Nosum.UnitTests.Wasm;

/// <summary>The "Entering the world" overlay (issue #208): the steps of the start, the tips, and the Try again action once the start is slow.</summary>
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the view model's timers die with the test.")]
public sealed class GameStartViewModelTests
{
    [Fact]
    public void Begin_ShowsTheConnectStepAtFifteenPercent()
    {
        using CultureScope scope = new("en");
        GameStartViewModel vm = new(Text());

        vm.Begin();

        vm.IsVisible.Should().BeTrue();
        vm.Percent.Should().Be(15);
        vm.StepText.Should().Be("Connecting to the server…");
        vm.IsSlow.Should().BeFalse();
    }

    [Fact]
    public void SetStep_MovesTheBarThroughTheStepsInOrder()
    {
        using CultureScope scope = new("en");
        GameStartViewModel vm = new(Text());
        vm.Begin();

        vm.SetStep(GameStartStep.Map);
        int atMap = vm.Percent;
        vm.SetStep(GameStartStep.Sprites);
        int atSprites = vm.Percent;
        vm.SetStep(GameStartStep.Draw);

        atMap.Should().Be(45);
        atSprites.Should().Be(75);
        vm.Percent.Should().Be(95);
    }

    [Fact]
    public void SetStep_BeforeBegin_IsIgnored()
    {
        GameStartViewModel vm = new(Text());

        vm.SetStep(GameStartStep.Draw);

        vm.IsVisible.Should().BeFalse();
        vm.Percent.Should().Be(15);
    }

    [Fact]
    public void MarkSlow_ShowsTheRetryAction_AndBeginStartsOver()
    {
        using CultureScope scope = new("en");
        GameStartViewModel vm = new(Text());
        vm.Begin();

        vm.MarkSlow();

        vm.IsSlow.Should().BeTrue();
        vm.SlowText.Should().Be("Still loading. If it takes too long, try again.");
        vm.RetryText.Should().Be("Try again");

        vm.End();
        vm.Begin();

        vm.IsSlow.Should().BeFalse();
    }

    [Fact]
    public void RotateTip_MovesToTheNextGameplayTip()
    {
        using CultureScope scope = new("en");
        GameStartViewModel vm = new(Text());
        vm.Begin();
        string first = vm.Tip;

        vm.RotateTip();

        vm.Tip.Should().Be(Text()[MapLoadingViewModel.TipKeys[1]].Value);
        vm.Tip.Should().NotBe(first);
    }

    [Fact]
    public void End_HidesTheOverlay_AndIgnoresLaterSteps()
    {
        using CultureScope scope = new("en");
        GameStartViewModel vm = new(Text());
        vm.Begin();

        vm.End();
        vm.SetStep(GameStartStep.Draw);

        vm.IsVisible.Should().BeFalse();
        vm.Percent.Should().Be(15);
    }
}
