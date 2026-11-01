// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Looks;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Shops;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The one movement multiplier (docs/adr/ADR-084): footwear × buff × running, clamped; boots, the tonic and the running saving.</summary>
public sealed class MovementSpeedTests
{
    // 20 x 10 tiles of 16 px, open ground.
    private static WalkGrid OpenGrid() => new(20, 10, 16, new bool[20 * 10]);

    [Theory]
    [InlineData(ItemCatalog.LeatherBoots, 1.05)]
    [InlineData(ItemCatalog.TravelerBoots, 1.10)]
    [InlineData(ItemCatalog.SwiftBoots, 1.15)]
    [InlineData(ItemCatalog.WingedBoots, 1.20)]
    [InlineData(0, 1.0)]
    [InlineData(ItemCatalog.Hoe, 1.0)]
    [InlineData(ItemCatalog.HatCap, 1.0)]
    public void FootwearFactor_EachBootsGivesItsPercent_AndNothingElseDoes(int itemId, double expected)
    {
        MovementSpeed.FootwearFactor(itemId).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void Combine_NothingActive_IsOne()
    {
        MovementSpeed.Combine(1, 1, 1).Should().Be(1);
    }

    [Fact]
    public void Combine_MultipliesTheFactors()
    {
        MovementSpeed.Combine(1.10, StatusEffectRules.SwiftnessSpeedFactor).Should().BeApproximately(1.375, 1e-9);
    }

    [Fact]
    public void Combine_StackAboveTheCap_IsClampedToMaxFactor()
    {
        MovementSpeed.Combine(1.20, StatusEffectRules.SwiftnessSpeedFactor, 1.3).Should().Be(MovementSpeed.MaxFactor);
        MovementSpeed.Combine(2, 2).Should().Be(MovementSpeed.MaxFactor);
    }

    [Fact]
    public void Combine_BelowOne_IsRaisedToOne()
    {
        MovementSpeed.Combine(1, 1, 0.5).Should().Be(1, "a combined multiplier never slows the base walk");
    }

    [Fact]
    public void For_WingedBootsWithSwiftness_IsTheWalkingCeiling()
    {
        // Walking has no run factor, so the highest walking stack (1.20 × 1.25) is 1.5, below the cap of 1.8 (docs/adr/ADR-084 amendment).
        MovementSpeed.For(ItemCatalog.WingedBoots, StatusEffectKind.Swiftness).Should().BeApproximately(1.5, 1e-9);
        MovementSpeed.MaxFactor.Should().Be(1.8);
    }

    [Fact]
    public void Product_IsNotClamped_SoRunningCanStillApplyTheRunFactor()
    {
        MovementSpeed.Product(ItemCatalog.WingedBoots, StatusEffectKind.Swiftness).Should().BeApproximately(1.5, 1e-9);
        MovementSpeed.Product(ItemCatalog.WingedBoots, StatusEffectKind.None).Should().BeApproximately(1.2, 1e-9);
        MovementSpeed.Product(0, StatusEffectKind.None).Should().Be(1);
    }

    [Theory]
    [InlineData(ItemCatalog.LeatherBoots, StatusEffectKind.None, 1.575)]
    [InlineData(ItemCatalog.TravelerBoots, StatusEffectKind.None, 1.65)]
    [InlineData(ItemCatalog.SwiftBoots, StatusEffectKind.None, 1.725)]
    [InlineData(ItemCatalog.WingedBoots, StatusEffectKind.None, 1.8)]
    [InlineData(0, StatusEffectKind.Swiftness, 1.8)]
    public void RunningMultiplier_EachBootsTierStillMatters(int feetItemId, StatusEffectKind buff, double expected)
    {
        // Each tier's boots raise the running pace: 1.05 / 1.10 / 1.15 / 1.20 times the 1.5 run factor, under the 1.8 cap; a tonic reaches the cap.
        RunRules.SpeedMultiplier(MovementSpeed.Product(feetItemId, buff), running: true).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void RunningMultiplier_NeverAboveTheCap()
    {
        RunRules.SpeedMultiplier(MovementSpeed.Product(ItemCatalog.WingedBoots, StatusEffectKind.Swiftness), running: true)
            .Should().Be(MovementSpeed.MaxFactor);
    }

    [Fact]
    public void For_BootsAloneAndBuffAloneAreTheirOwnFactor()
    {
        MovementSpeed.For(ItemCatalog.LeatherBoots, StatusEffectKind.None).Should().BeApproximately(1.05, 1e-9);
        MovementSpeed.For(0, StatusEffectKind.Swiftness).Should().BeApproximately(StatusEffectRules.SwiftnessSpeedFactor, 1e-9);
    }

    [Fact]
    public void Validate_SustainedPaceAtTheCap_IsAccepted()
    {
        double factor = MovementSpeed.For(ItemCatalog.WingedBoots, StatusEffectKind.Swiftness);
        MotionState state = new(20, 80, 0);
        WalkGrid grid = OpenGrid();
        double pace = Walker.Speed * factor * 0.1;

        for (int i = 1; i <= 10; i++)
        {
            MoveValidator.Validate(ref state, 20 + (pace * i), 80, 1, true, i * 0.1, grid, factor).Should().Be(MoveVerdict.Accepted, $"step {i}");
        }
    }

    [Fact]
    public void Validate_PaceAboveTheCap_IsRefusedOnceTheBudgetRunsOut()
    {
        double factor = MovementSpeed.For(ItemCatalog.WingedBoots, StatusEffectKind.Swiftness);
        MotionState state = new(20, 80, 0);
        WalkGrid grid = OpenGrid();
        // Twice the walking pace drains the budget faster than the boots refill it, so the refusal comes within a few dozen steps.
        double pace = Walker.Speed * 2 * 0.1;

        MoveVerdict last = MoveVerdict.Accepted;
        for (int i = 1; i <= 60 && last == MoveVerdict.Accepted; i++)
        {
            last = MoveValidator.Validate(ref state, 20 + (pace * i), 80, 1, true, i * 0.1, grid, factor);
        }

        last.Should().Be(MoveVerdict.TooFast);
    }

    [Fact]
    public void RunEnergyCost_SwiftAndWingedBootsSaveTheirShare()
    {
        FootwearRules.RunEnergyCost(10, ItemCatalog.SwiftBoots).Should().Be(8);
        FootwearRules.RunEnergyCost(10, ItemCatalog.WingedBoots).Should().Be(7);
    }

    [Theory]
    [InlineData(ItemCatalog.LeatherBoots)]
    [InlineData(ItemCatalog.TravelerBoots)]
    [InlineData(0)]
    [InlineData(ItemCatalog.Hoe)]
    public void RunEnergyCost_WithoutASavingFootwear_IsTheBaseCost(int itemId)
    {
        FootwearRules.RunEnergyCost(10, itemId).Should().Be(10);
    }

    [Fact]
    public void RunEnergyCost_NegativeBase_Throws()
    {
        Action act = () => FootwearRules.RunEnergyCost(-1, ItemCatalog.SwiftBoots);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Catalogue_TheBootsAreFootwearOfTheirTier()
    {
        foreach (int id in new[] { ItemCatalog.LeatherBoots, ItemCatalog.TravelerBoots, ItemCatalog.SwiftBoots, ItemCatalog.WingedBoots })
        {
            ItemCatalog.TryGet(id, out ItemDefinition? boots).Should().BeTrue();
            boots!.Kind.Should().Be(ItemKind.Clothing);
            boots.Wear.Should().Be(WearSlot.Feet);
            boots.MaxStack.Should().Be(1);
        }
    }

    [Fact]
    public void Tonic_IsASwiftnessDraughtForThreeGameHours()
    {
        // Three game hours at the default day length: a game hour is a twenty-fourth of the 12 h day, so 30 real minutes.
        StatusEffectRules.SpeedTonicSeconds.Should().Be(5400);
        CombatRules.PotionEffect(ItemCatalog.SpeedTonic).Should().Be((StatusEffectKind.Swiftness, 5400, 1));
    }

    [Fact]
    public void Shops_TheTailorSellsTheTwoEntryPairs()
    {
        ShopDefinition tailor = ShopCatalog.All.Single(shop => shop.Id == ShopCatalog.ClothingShop);

        tailor.TryGetPrice(ItemCatalog.LeatherBoots, out int leather).Should().BeTrue();
        tailor.TryGetPrice(ItemCatalog.TravelerBoots, out int traveler).Should().BeTrue();
        leather.Should().BeLessThan(traveler);
        tailor.TryGetPrice(ItemCatalog.WingedBoots, out _).Should().BeFalse("the faster pairs are crafted, not sold");
    }

    [Fact]
    public void PlayerLook_FeetAreWornAndTakenOffApartFromTheHat()
    {
        PlayerLook look = PlayerLook.Default.With(WearSlot.Feet, ItemCatalog.SwiftBoots).With(WearSlot.Hat, ItemCatalog.HatCap);

        look.Worn(WearSlot.Feet).Should().Be(ItemCatalog.SwiftBoots);
        look.Worn(WearSlot.Hat).Should().Be(ItemCatalog.HatCap);
        look.With(WearSlot.Feet, 0).Worn(WearSlot.Feet).Should().Be(0);
        PlayerLook.Default.FeetItemId.Should().Be(0, "a new player walks in bare feet");
    }
}
