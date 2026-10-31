// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Items;
using Xunit;
using static Nosum.UnitTests.Wasm.SocialClientFixtures;

namespace Nosum.UnitTests.Wasm;

[Collection(SharedUiCulture.Name)]
public sealed class ItemTooltipTests
{
    [Fact]
    public void Describe_WoodenSword_ShowsItsDamage()
    {
        using CultureScope scope = new("en");

        string tooltip = WasmTestSupport.Describer().Describe(0, ItemCatalog.WoodenSword, 1).Tooltip;

        tooltip.Should().Contain("Damage: 12");
        tooltip.Should().Contain("Tier: Basic");
    }

    [Fact]
    public void Describe_Parsnip_ShowsTheEnergyItRestoresAndItsSellPrice()
    {
        using CultureScope scope = new("en");

        string tooltip = WasmTestSupport.Describer().Describe(0, ItemCatalog.Parsnip, 1).Tooltip;

        tooltip.Should().Contain("Restores energy: +10");
        tooltip.Should().Contain("Sells for 40 coins");
    }

    [Fact]
    public void Describe_Parsnip_ShowsTheEnergyInVietnameseInTheVietnameseCulture()
    {
        using CultureScope scope = new("vi");

        string tooltip = WasmTestSupport.Describer().Describe(0, ItemCatalog.Parsnip, 1).Tooltip;

        tooltip.Should().Contain("Hồi sức lực: +10");
    }

    [Fact]
    public void Describe_CopperAxe_ShowsItsTierAndDamagePerSwing()
    {
        using CultureScope scope = new("en");

        string tooltip = WasmTestSupport.Describer().Describe(0, ItemCatalog.CopperAxe, 1).Tooltip;

        tooltip.Should().Contain("Tier: Copper");
        tooltip.Should().Contain("Resource damage: 2 per swing");
    }

    [Fact]
    public void Describe_CopperWateringCan_ShowsItsWaterCapacity()
    {
        using CultureScope scope = new("en");

        string tooltip = WasmTestSupport.Describer().Describe(0, ItemCatalog.CopperWateringCan, 1).Tooltip;

        tooltip.Should().Contain("Water capacity: 55");
    }

    [Fact]
    public void Describe_Hoe_ShowsNoEnergyNoDamageAndNoSellPriceLine()
    {
        using CultureScope scope = new("en");

        string tooltip = WasmTestSupport.Describer().Describe(0, ItemCatalog.Hoe, 1).Tooltip;

        tooltip.Should().NotContain("Restores energy");
        tooltip.Should().NotContain("Damage:");
        tooltip.Should().NotContain("Sells for");
    }

    [Fact]
    public void Describe_StackedParsnips_ShowTheQuantityAfterTheStatRows()
    {
        using CultureScope scope = new("en");

        string tooltip = WasmTestSupport.Describer().Describe(0, ItemCatalog.Parsnip, 8).Tooltip;

        tooltip.Should().EndWith("Quantity: 8");
    }
}
