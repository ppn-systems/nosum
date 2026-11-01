// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Placeable;
using Nosum.Gameplay.Items;
using Nosum.Wasm.ViewModels.Placement;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class PlacementViewModelTests
{
    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    [Fact]
    public void Requests_AreCollectedOnceAndTakenByTheFrame()
    {
        PlacementViewModel vm = new(Localizer<Catalogue>(), Localizer<Language>());

        vm.Request(PlacementRequests.Rotate);
        vm.Request(PlacementRequests.PickUp);

        vm.TakeRequests().Should().Be(PlacementRequests.Rotate | PlacementRequests.PickUp);
        vm.TakeRequests().Should().Be(PlacementRequests.None);
    }

    [Fact]
    public void SetActiveAndSetCarrying_RaiseChangedOnlyWhenTheStateChanges()
    {
        PlacementViewModel vm = new(Localizer<Catalogue>(), Localizer<Language>());
        int changed = 0;
        vm.Changed += () => changed++;

        vm.SetActive(true);
        vm.SetActive(true);
        vm.SetCarrying(ItemCatalog.OakChair);
        vm.SetCarrying(ItemCatalog.OakChair);

        changed.Should().Be(2);
        vm.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void CarryingText_NamesTheItemInTheCurrentLanguage()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            PlacementViewModel vm = new(Localizer<Catalogue>(), Localizer<Language>());
            vm.CarryingText.Should().BeNull();

            vm.SetCarrying(ItemCatalog.OakChair);

            vm.CarryingText.Should().Be("Carrying: Oak chair");
            vm.GrabLabel.Should().Be("Cancel move (F)");
            vm.SetCarrying(0);
            vm.GrabLabel.Should().Be("Move (F)");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
    [Fact]
    public void PlaceRequest_IsCollectedLikeTheOtherRequests()
    {
        PlacementViewModel vm = new(Localizer<Catalogue>(), Localizer<Language>());

        vm.Request(PlacementRequests.Place);

        vm.TakeRequests().Should().Be(PlacementRequests.Place);
        vm.TakeRequests().Should().Be(PlacementRequests.None);
    }

    [Fact]
    public void NoticeText_ShowsTheReasonAnInvalidGhostCannotBePlaced_AndNothingWhenValidOrHidden()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            PlacementViewModel vm = new(Localizer<Catalogue>(), Localizer<Language>());

            vm.SetGhost(shown: true, PlaceStatus.Blocked, confirmingBuild: false);
            vm.NoticeText.Should().Be("Something is in the way.");
            vm.NoticeIsError.Should().BeTrue();

            vm.SetGhost(shown: true, PlaceStatus.Ok, confirmingBuild: false);
            vm.NoticeText.Should().BeNull();
            vm.NoticeIsError.Should().BeFalse();

            vm.SetGhost(shown: false, PlaceStatus.Blocked, confirmingBuild: true);
            vm.NoticeText.Should().BeNull();
            vm.GhostStatus.Should().Be(PlaceStatus.Ok);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void NoticeText_AsksForASecondPressWhileABuildingIsConfirmed()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            PlacementViewModel vm = new(Localizer<Catalogue>(), Localizer<Language>());
            int changed = 0;
            vm.Changed += () => changed++;

            vm.SetGhost(shown: true, PlaceStatus.Ok, confirmingBuild: true);

            vm.ConfirmingBuild.Should().BeTrue();
            vm.NoticeIsError.Should().BeFalse();
            vm.NoticeText.Should().Contain("Press E again");
            vm.SetGhost(shown: true, PlaceStatus.Ok, confirmingBuild: true);
            changed.Should().Be(1, "an unchanged ghost raises nothing");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
