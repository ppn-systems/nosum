// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Visits;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class GuestbookRulesTests
{
    private static readonly Guid s_owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_visitor = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Logs_VisitorWateringAFarmThatHasAGuestbook_IsLogged()
    {
        GuestbookRules.Logs(new MapInstance(GameMaps.Farm, s_owner), s_visitor, guestbookStands: true).Should().BeTrue();
        GuestbookRules.Logs(new MapInstance(GameMaps.FarmHouse, s_owner), s_visitor, guestbookStands: true).Should().BeTrue();
    }

    [Fact]
    public void Logs_VisitorWateringAFarmWithoutAGuestbook_IsNotLogged()
    {
        GuestbookRules.Logs(new MapInstance(GameMaps.Farm, s_owner), s_visitor, guestbookStands: false).Should().BeFalse();
    }

    [Fact]
    public void Logs_OwnerWateringTheirOwnFarm_IsNeverLogged()
    {
        GuestbookRules.Logs(new MapInstance(GameMaps.Farm, s_owner), s_owner, guestbookStands: true).Should().BeFalse();
    }

    [Fact]
    public void Logs_WateringOnASharedMap_IsNotLogged()
    {
        GuestbookRules.Logs(MapInstance.Shared(GameMaps.Town), s_visitor, guestbookStands: true).Should().BeFalse();
    }

    [Fact]
    public void MaxRows_IsThirty()
    {
        GuestbookRules.MaxRows.Should().Be(30);
    }
}
