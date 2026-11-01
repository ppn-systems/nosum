// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Contracts;
using Nosum.Contracts.Market;
using Nosum.Contracts.Market.C2S;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>The browse order on the wire (docs/adr/ADR-013 amendment 2026-11-01, protocol 30): the field is appended and its values are append-only.</summary>
public sealed class MarketSortPacketTests
{
    [Theory]
    [InlineData(MarketSort.Newest)]
    [InlineData(MarketSort.UnitPriceLowest)]
    [InlineData(MarketSort.UnitPriceHighest)]
    public void BrowseListings_SortBy_RoundTrips(MarketSort sort)
    {
        using BrowseListings request = new() { Page = 2, SortBy = (int)sort };
        request.ItemIds.AddRange([200, 201]);

        BrowseListings copy = BrowseListings.Deserialize(request.Serialize());

        copy.Page.Should().Be(2);
        copy.ItemIds.Should().Equal(200, 201);
        ((MarketSort)copy.SortBy).Should().Be(sort);
    }

    [Fact]
    public void BrowseListings_WithoutASort_IsNewestFirst()
    {
        using BrowseListings request = new() { Page = 0 };

        BrowseListings.Deserialize(request.Serialize()).SortBy.Should().Be((int)MarketSort.Newest);
    }

    [Fact]
    public void MarketSort_Values_AreAppendOnly()
    {
        // Each value is on the wire: a renumbering would silently reorder every browse page.
        ((byte)MarketSort.Newest).Should().Be(0);
        ((byte)MarketSort.UnitPriceLowest).Should().Be(1);
        ((byte)MarketSort.UnitPriceHighest).Should().Be(2);
    }

    [Fact]
    public void NosumProtocol_Version_IsAtLeastThirtyOne_WithTheDaysLeftOnAnUnripeCrop()
    {
        NosumProtocol.Version.Should().BeGreaterThanOrEqualTo(31);
    }
}
