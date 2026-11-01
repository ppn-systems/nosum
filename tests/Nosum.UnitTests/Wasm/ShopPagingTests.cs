// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Wasm.ViewModels.Shop;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The shop list's paging math: how many whole rows fit the window, and which page holds which offer (docs/design/ui-standard.md section 3).</summary>
public sealed class ShopPagingTests
{
    // One row at 1280x720 (18 u at u = 3) and the 2 u gap between rows.
    private const double Row720 = 54;
    private const double Gap720 = 6;

    [Theory]
    [InlineData(248, 4)] // the 1280x720 room with the current overhead: 4 rows fit, a fifth needs 60 px more
    [InlineData(294, 5)] // room for five rows exactly: 5 x 54 + 4 x 6
    [InlineData(293, 4)] // one pixel short of five rows
    [InlineData(474, 8)] // room for eight rows exactly: 8 x 54 + 7 x 6
    [InlineData(2000, 8)] // more room than eight rows: clamped to the design goal
    [InlineData(50, 3)] // less room than three rows: clamped to the floor so a page is never empty
    [InlineData(0, 3)]
    [InlineData(-30, 3)]
    public void RowsThatFit_AtRowHeight54_CountsWholeRowsClampedToFourToEight(double available, int expected)
    {
        ShopViewModel.RowsThatFit(available, Row720, Gap720).Should().Be(expected);
    }

    [Fact]
    public void RowsThatFit_SubPixelRounding_DoesNotLoseARowThatFitsExactly()
    {
        ShopViewModel.RowsThatFit(294 - 1e-9, Row720, Gap720).Should().Be(5);
    }

    [Fact]
    public void RowsThatFit_UnlimitedRoom_IsTheMaximum()
    {
        ShopViewModel.RowsThatFit(double.PositiveInfinity, Row720, Gap720).Should().Be(ShopViewModel.MaxRowsPerPage);
    }

    [Fact]
    public void RowsThatFit_UnmeasuredRow_IsTheMinimum()
    {
        ShopViewModel.RowsThatFit(500, 0, Gap720).Should().Be(ShopViewModel.MinRowsPerPage);
    }

    [Fact]
    public void RowsThatFit_NaNRoom_IsTheMinimum()
    {
        ShopViewModel.RowsThatFit(double.NaN, Row720, Gap720).Should().Be(ShopViewModel.MinRowsPerPage);
    }

    [Fact]
    public void RowsThatFit_Phone390Room_IsEightRows()
    {
        // 390 x 844: a row is 36 px with a 4 px gap, and the window runs to the bottom of the screen.
        ShopViewModel.RowsThatFit(458, 36, 4).Should().Be(8);
    }

    [Theory]
    [InlineData(1, 8)] // one column: up to eight rows of one item
    [InlineData(2, 4)] // two columns: four rows hold the eight items of a page
    [InlineData(3, 4)] // more columns than the list shows: the two-column limit
    [InlineData(0, 8)] // no column: taken as one
    public void RowLimit_Columns_KeepsAPageAtMostEightItems(int columns, int expected)
    {
        ShopViewModel.RowLimit(columns).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(4, 4, 1)]
    [InlineData(5, 4, 2)]
    [InlineData(8, 4, 2)]
    [InlineData(8, 8, 1)]
    [InlineData(9, 8, 2)]
    public void PagesFor_OffersAndRowsPerPage_CountsPagesOfWholeRows(int offers, int rowsPerPage, int expected)
    {
        ShopViewModel.PagesFor(offers, rowsPerPage).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 4, 0)]
    [InlineData(3, 4, 0)]
    [InlineData(4, 4, 1)]
    [InlineData(7, 8, 0)]
    [InlineData(8, 8, 1)]
    public void PageOf_FirstOfferAndRowsPerPage_IsTheZeroBasedPageThatHoldsIt(int firstOffer, int rowsPerPage, int expected)
    {
        ShopViewModel.PageOf(firstOffer, rowsPerPage).Should().Be(expected);
    }
}
