// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;
using Nosum.Wasm.ViewModels.Feedback;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class FarmSeasonNoticeTests
{
    [Fact]
    public void SeasonsOf_SeedWithThreeSeasons_ListsThemInCalendarOrder()
    {
        FarmSeasonNotice.SeasonsOf(ItemCatalog.ParsnipSeeds).Should().Equal(Season.Spring, Season.Fall, Season.Winter);
    }

    [Fact]
    public void SeasonsOf_SeedWithOneSeason_ListsThatSeason()
    {
        FarmSeasonNotice.SeasonsOf(ItemCatalog.PotatoSeeds).Should().Equal(Season.Spring);
        FarmSeasonNotice.SeasonsOf(ItemCatalog.TomatoSeeds).Should().Equal(Season.Summer);
    }

    [Theory]
    [InlineData(ItemCatalog.WateringCan)]
    [InlineData(0)]
    public void SeasonsOf_ItemThatIsNotASeed_IsEmpty(int itemId)
    {
        FarmSeasonNotice.SeasonsOf(itemId).Should().BeEmpty();
    }
}
