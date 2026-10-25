// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Gameplay.Visits;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class VisitRulesFriendTests
{
    private static readonly Guid s_owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_visitor = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void MayEnter_Friend_IsLetInByDefault()
    {
        VisitRules.FriendsMayVisitByDefault.Should().BeTrue();

        VisitRules.MayEnter(s_visitor, s_owner, false, false, false, true, isFriend: true).Should().BeTrue();
    }

    [Fact]
    public void MayEnter_Friend_IsRefusedWhenTheOwnerTurnedFriendsOff()
    {
        VisitRules.MayEnter(s_visitor, s_owner, false, false, false, true, isFriend: true, friendsMayVisit: false).Should().BeFalse();
    }

    [Fact]
    public void MayEnter_FriendOffButListedOrInTheCoop_StillGetsIn()
    {
        VisitRules.MayEnter(s_visitor, s_owner, false, true, false, true, isFriend: true, friendsMayVisit: false).Should().BeTrue();
        VisitRules.MayEnter(s_visitor, s_owner, false, false, true, true, isFriend: true, friendsMayVisit: false).Should().BeTrue();
    }

    [Fact]
    public void MayEnter_BlockedVisitor_IsRefusedWhateverElseLetsThemIn()
    {
        VisitRules.MayEnter(s_visitor, s_owner, false, listed: true, sameCoop: true, coopMembersMayVisit: true, isFriend: true, friendsMayVisit: true, blocked: true).Should().BeFalse();
    }

    [Fact]
    public void MayEnter_GuestOwner_NeverLetsAFriendIn()
    {
        VisitRules.MayEnter(s_visitor, s_owner, true, false, false, true, isFriend: true).Should().BeFalse();
    }

    [Fact]
    public void MayEnter_Stranger_StaysOut()
    {
        VisitRules.MayEnter(s_visitor, s_owner, false, false, false, true).Should().BeFalse();
    }

    [Fact]
    public void MayEnter_Owner_IsAlwaysLetIn_EvenWhenBlockedByAMistake()
    {
        VisitRules.MayEnter(s_owner, s_owner, false, false, false, true, blocked: true).Should().BeTrue();
    }
}
