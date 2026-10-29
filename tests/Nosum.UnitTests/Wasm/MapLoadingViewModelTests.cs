// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Nosum.Wasm.ViewModels.Maps;
using Xunit;
using static Nosum.UnitTests.Wasm.WasmTestSupport;

namespace Nosum.UnitTests.Wasm;

/// <summary>The loading overlay shown while a map the server sent the player to loads (the black screen of a map change).</summary>
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the view model's timer dies with the test.")]
public sealed class MapLoadingViewModelTests
{
    [Fact]
    public void Begin_ShowsOverlayAtZeroPercent_AndRaisesChanged()
    {
        MapLoadingViewModel vm = new(Text());
        int changes = 0;
        vm.Changed += () => changes++;

        vm.Begin();

        vm.IsVisible.Should().BeTrue();
        vm.Percent.Should().Be(0);
        changes.Should().Be(1);
    }

    [Fact]
    public void Begin_WhenAlreadyVisible_DoesNotRaiseChangedAgain()
    {
        MapLoadingViewModel vm = new(Text());
        vm.Begin();
        int changes = 0;
        vm.Changed += () => changes++;

        vm.Begin();

        changes.Should().Be(0);
    }

    [Fact]
    public void SetPercent_WhileVisible_ClampsToZeroToHundred()
    {
        MapLoadingViewModel vm = new(Text());
        vm.Begin();

        vm.SetPercent(150);
        vm.Percent.Should().Be(100);

        vm.SetPercent(-5);
        vm.Percent.Should().Be(0);
    }

    [Fact]
    public void SetPercent_WhileHidden_IsIgnored()
    {
        MapLoadingViewModel vm = new(Text());
        int changes = 0;
        vm.Changed += () => changes++;

        vm.SetPercent(40);

        vm.Percent.Should().Be(0);
        changes.Should().Be(0);
    }

    [Fact]
    public void SetPercent_WithSameValue_DoesNotRaiseChanged()
    {
        MapLoadingViewModel vm = new(Text());
        vm.Begin();
        vm.SetPercent(40);
        int changes = 0;
        vm.Changed += () => changes++;

        vm.SetPercent(40);

        changes.Should().Be(0);
    }

    [Fact]
    public void End_HidesOverlay_AndRaisesChanged()
    {
        MapLoadingViewModel vm = new(Text());
        vm.Begin();
        int changes = 0;
        vm.Changed += () => changes++;

        vm.End();

        vm.IsVisible.Should().BeFalse();
        changes.Should().Be(1);
    }

    [Fact]
    public void Tip_AndTitle_AreLocalizedText()
    {
        MapLoadingViewModel vm = new(Text());

        vm.Tip.Should().NotBeNullOrWhiteSpace().And.NotStartWith("Map_");
        vm.Title.Should().NotBeNullOrWhiteSpace().And.NotStartWith("Map_");
    }
}
