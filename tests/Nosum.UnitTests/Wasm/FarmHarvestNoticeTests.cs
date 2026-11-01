// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Contracts.Farm;
using Nosum.Gameplay.Items;
using Nosum.Wasm.ViewModels.Feedback;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class FarmHarvestNoticeTests
{
    [Theory]
    [InlineData(ItemCatalog.Scythe, false)]
    [InlineData(0, false)]
    [InlineData(ItemCatalog.WateringCan, true)]
    public void NamesHarvest_OkHarvestOfOneTile_NamesItOnlyWithAnotherItemInHand(int heldItemId, bool expected)
    {
        FarmHarvestNotice.NamesHarvest(FarmStatus.Ok, FarmAction.Harvested, 1, heldItemId).Should().Be(expected);
    }

    [Fact]
    public void NamesHarvest_TapWithWateringCan_NamesTheHarvestTooNotOnlyTheActionKey()
    {
        // A mouse tap with the can on a ripe crop harvests it: the same line as the action key's harvest (docs/adr/ADR-095 D2).
        FarmHarvestNotice.NamesHarvest(FarmStatus.Ok, FarmAction.Harvested, 1, ItemCatalog.WateringCan).Should().BeTrue();
    }

    [Theory]
    [InlineData(FarmStatus.NotMature, FarmAction.None)]
    [InlineData(FarmStatus.Ok, FarmAction.Watered)]
    [InlineData(FarmStatus.Ok, FarmAction.Planted)]
    public void NamesHarvest_OtherOutcomes_DoesNotName(FarmStatus status, FarmAction action)
    {
        FarmHarvestNotice.NamesHarvest(status, action, 1, ItemCatalog.WateringCan).Should().BeFalse();
    }

    [Fact]
    public void NamesHarvest_WideUse_DoesNotName()
    {
        // A line of harvests is counted by the wide use's own count (docs/adr/ADR-088 D1), not named one tile at a time.
        FarmHarvestNotice.NamesHarvest(FarmStatus.Ok, FarmAction.Harvested, 3, ItemCatalog.WateringCan).Should().BeFalse();
    }
}
