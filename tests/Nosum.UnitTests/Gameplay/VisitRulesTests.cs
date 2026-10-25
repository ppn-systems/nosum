// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Visits;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class VisitRulesTests
{
    private static readonly Guid s_owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_visitor = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(PlaceAction.Walk)]
    [InlineData(PlaceAction.Water)]
    [InlineData(PlaceAction.Till)]
    [InlineData(PlaceAction.Plant)]
    [InlineData(PlaceAction.Harvest)]
    [InlineData(PlaceAction.PlaceObject)]
    [InlineData(PlaceAction.MoveObject)]
    [InlineData(PlaceAction.PickUpObject)]
    [InlineData(PlaceAction.UseShippingBin)]
    public void MayDo_Owner_InTheirOwnPlace_CanDoEverything(PlaceAction action)
    {
        VisitRules.MayDo(new MapInstance(GameMaps.Farm, s_owner), s_owner, action).Should().BeTrue();
    }

    [Theory]
    [InlineData(PlaceAction.Walk, true)]
    [InlineData(PlaceAction.Water, true)]
    [InlineData(PlaceAction.Till, false)]
    [InlineData(PlaceAction.Plant, false)]
    [InlineData(PlaceAction.Harvest, false)]
    [InlineData(PlaceAction.PlaceObject, false)]
    [InlineData(PlaceAction.MoveObject, false)]
    [InlineData(PlaceAction.PickUpObject, false)]
    [InlineData(PlaceAction.UseShippingBin, false)]
    public void MayDo_Visitor_InSomeoneElsesPlace_CanOnlyWalkAndWater(PlaceAction action, bool allowed)
    {
        VisitRules.MayDo(new MapInstance(GameMaps.Farm, s_owner), s_visitor, action).Should().Be(allowed);
        VisitRules.MayDo(new MapInstance(GameMaps.FarmHouse, s_owner), s_visitor, action).Should().Be(allowed);
    }

    [Fact]
    public void MayDo_SharedMap_HasNoOwnerSoNothingIsRestrictedHere()
    {
        VisitRules.MayDo(MapInstance.Shared(GameMaps.Town), s_visitor, PlaceAction.PlaceObject).Should().BeTrue();
    }

    [Fact]
    public void IsVisitor_OnlyInAPrivatePlaceOfSomeoneElse()
    {
        VisitRules.IsVisitor(new MapInstance(GameMaps.Farm, s_owner), s_visitor).Should().BeTrue();
        VisitRules.IsVisitor(new MapInstance(GameMaps.Farm, s_owner), s_owner).Should().BeFalse();
        VisitRules.IsVisitor(MapInstance.Shared(GameMaps.Town), s_visitor).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false, false, true, false)] // a stranger
    [InlineData(false, true, false, true, true)]   // listed
    [InlineData(false, false, true, true, true)]   // same co-op, setting on
    [InlineData(false, false, true, false, false)] // same co-op, setting off
    [InlineData(false, true, true, false, true)]   // listed beats the setting
    [InlineData(true, true, true, true, false)]    // a guest's place is private whatever the rows say
    public void MayEnter_OtherPlayersPlace_NeedsTheListOrTheCoopSetting(bool ownerIsGuest, bool listed, bool sameCoop, bool coopSetting, bool expected)
    {
        VisitRules.MayEnter(s_visitor, s_owner, ownerIsGuest, listed, sameCoop, coopSetting).Should().Be(expected);
    }

    [Fact]
    public void MayEnter_OwnPlace_IsAlwaysAllowed()
    {
        VisitRules.MayEnter(s_owner, s_owner, ownerIsGuest: true, listed: false, sameCoop: false, coopMembersMayVisit: false).Should().BeTrue();
    }

    [Fact]
    public void Defaults_CoopMembersMayVisit_AndTheListHoldsFifty()
    {
        VisitRules.CoopMembersMayVisitByDefault.Should().BeTrue();
        VisitRules.MaxListed.Should().Be(50);
    }
}
