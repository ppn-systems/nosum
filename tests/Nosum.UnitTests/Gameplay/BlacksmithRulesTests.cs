// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Blacksmith;
using Nosum.Gameplay.Items;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The blacksmith's prices and timing (docs/adr/ADR-026 D2).</summary>
public sealed class BlacksmithRulesTests
{
    [Theory]
    [InlineData(ItemCatalog.Hoe, ItemCatalog.CopperHoe, 2_000, ItemCatalog.CopperBar)]
    [InlineData(ItemCatalog.WateringCan, ItemCatalog.CopperWateringCan, 2_000, ItemCatalog.CopperBar)]
    [InlineData(ItemCatalog.Axe, ItemCatalog.CopperAxe, 2_000, ItemCatalog.CopperBar)]
    [InlineData(ItemCatalog.Pickaxe, ItemCatalog.CopperPickaxe, 2_000, ItemCatalog.CopperBar)]
    [InlineData(ItemCatalog.CopperHoe, ItemCatalog.IronHoe, 8_000, ItemCatalog.IronBar)]
    [InlineData(ItemCatalog.CopperWateringCan, ItemCatalog.IronWateringCan, 8_000, ItemCatalog.IronBar)]
    [InlineData(ItemCatalog.CopperAxe, ItemCatalog.IronAxe, 8_000, ItemCatalog.IronBar)]
    [InlineData(ItemCatalog.CopperPickaxe, ItemCatalog.IronPickaxe, 8_000, ItemCatalog.IronBar)]
    public void TryQuote_CopperCostsTwoThousandAndFiveCopperBars_IronEightThousandAndFiveIronBars(int from, int to, long coins, int bar)
    {
        BlacksmithRules.TryQuote(from, out UpgradeQuote? found).Should().BeTrue();

        UpgradeQuote quote = found!.Value;
        quote.FromItemId.Should().Be(from);
        quote.ToItemId.Should().Be(to);
        quote.Coins.Should().Be(coins);
        quote.BarItemId.Should().Be(bar);
        quote.BarCount.Should().Be(5);
        ToolRules.TierOf(to).Should().Be(quote.ToTier).And.Be(ToolRules.TierOf(from) + 1);
    }

    [Theory]
    [InlineData(ItemCatalog.IronHoe)]
    [InlineData(ItemCatalog.IronPickaxe)]
    [InlineData(ItemCatalog.Scythe)]
    [InlineData(ItemCatalog.Wood)]
    [InlineData(ItemCatalog.ParsnipSeeds)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(99999)]
    public void TryQuote_TheBestTierNonToolsAndTheScythe_CannotBeUpgraded(int itemId)
    {
        BlacksmithRules.TryQuote(itemId, out UpgradeQuote? quote).Should().BeFalse();

        quote.Should().BeNull();
    }

    [Fact]
    public void ReadyDay_IsTwoGameDaysAfterHandingTheToolIn()
    {
        BlacksmithRules.UpgradeDays.Should().Be(2);
        BlacksmithRules.ReadyDay(10).Should().Be(12);
        BlacksmithRules.IsReady(12, 11).Should().BeFalse();
        BlacksmithRules.IsReady(12, 12).Should().BeTrue();
        BlacksmithRules.IsReady(12, 40).Should().BeTrue();
    }
}
