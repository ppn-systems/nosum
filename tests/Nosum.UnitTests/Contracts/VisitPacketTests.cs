// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Market;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Visit;
using Nosum.Contracts.Visit.C2S;
using Nosum.Contracts.Visit.S2C;
using Xunit;

namespace Nosum.UnitTests.Contracts;

public sealed class VisitPacketTests
{
    [Fact]
    public void VisitOpCode_Values_AreFrozenInTheirBand()
    {
        // Append-only (docs/adr/ADR-019 D8): these numbers never change and are never reused.
        VisitOpCode.GetVisitors.Should().Be(0xE000);
        VisitOpCode.AddVisitor.Should().Be(0xE001);
        VisitOpCode.RemoveVisitor.Should().Be(0xE002);
        VisitOpCode.KickVisitor.Should().Be(0xE003);
        VisitOpCode.SetCoopVisits.Should().Be(0xE004);
        VisitOpCode.VisitPlayer.Should().Be(0xE005);
        VisitOpCode.VisitResult.Should().Be(0xE040);
        VisitOpCode.VisitorsSnapshot.Should().Be(0xE041);
    }

    [Fact]
    public void VisitOpCode_AllValues_AreUniqueAndInTheReservedBand()
    {
        ushort[] values = [.. typeof(VisitOpCode).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (ushort)f.GetRawConstantValue()!)];

        values.Should().OnlyHaveUniqueItems();
        values.Should().OnlyContain(v => v >= 0xE000 && v <= 0xE0FF);
        values.Where(v => v <= 0xE03F).Should().HaveCount(6, "requests use the lower half");
    }

    [Fact]
    public void StaticOpCodes_OfEveryVisitPacket_MatchTheirConstants()
    {
        GetVisitors.StaticOpCode.Should().Be(VisitOpCode.GetVisitors);
        AddVisitor.StaticOpCode.Should().Be(VisitOpCode.AddVisitor);
        RemoveVisitor.StaticOpCode.Should().Be(VisitOpCode.RemoveVisitor);
        KickVisitor.StaticOpCode.Should().Be(VisitOpCode.KickVisitor);
        SetCoopVisits.StaticOpCode.Should().Be(VisitOpCode.SetCoopVisits);
        VisitPlayer.StaticOpCode.Should().Be(VisitOpCode.VisitPlayer);
        VisitResult.StaticOpCode.Should().Be(VisitOpCode.VisitResult);
        VisitorsSnapshot.StaticOpCode.Should().Be(VisitOpCode.VisitorsSnapshot);
    }

    [Fact]
    public void VisitStatus_Values_AreAppendOnly()
    {
        ((byte)VisitStatus.Ok).Should().Be(0);
        ((byte)VisitStatus.InvalidNickname).Should().Be(1);
        ((byte)VisitStatus.UnknownPlayer).Should().Be(2);
        ((byte)VisitStatus.Self).Should().Be(3);
        ((byte)VisitStatus.GuestOwner).Should().Be(4);
        ((byte)VisitStatus.AlreadyListed).Should().Be(5);
        ((byte)VisitStatus.NotListed).Should().Be(6);
        ((byte)VisitStatus.ListFull).Should().Be(7);
        ((byte)VisitStatus.NotAllowed).Should().Be(8);
        ((byte)VisitStatus.NotOnPublicMap).Should().Be(9);
        ((byte)VisitStatus.NotJoined).Should().Be(10);
        ((byte)VisitStatus.Conflict).Should().Be(11);
    }

    [Fact]
    public void RefusalCodesAddedForVisitors_AppendAfterTheExistingOnes()
    {
        ((byte)FarmStatus.NotYourFarm).Should().Be(14);
        ((byte)PlaceStatus.NotYourPlace).Should().Be(14);
        ((byte)MarketStatus.NotYourBin).Should().Be(18);
        ((byte)FarmStatus.Conflict).Should().Be(13, "existing values did not move");
        ((byte)PlaceStatus.Conflict).Should().Be(13);
        ((byte)MarketStatus.TooBig).Should().Be(17);
    }

    [Fact]
    public void VisitorsSnapshot_WithListedAndPresentVisitors_RoundTrips()
    {
        Guid listed = Guid.CreateVersion7();
        Guid coop = Guid.CreateVersion7();
        using VisitorsSnapshot packet = new() { CoopMembersMayVisit = false, IsGuest = true };
        packet.Visitors.Add(new VisitorRow { PlayerId = listed, Nickname = "Lan Anh", Listed = true, Present = false });
        packet.Visitors.Add(new VisitorRow { PlayerId = coop, Nickname = "guest-0042", Listed = false, Present = true });

        VisitorsSnapshot copy = VisitorsSnapshot.Deserialize(packet.Serialize());

        copy.CoopMembersMayVisit.Should().BeFalse();
        copy.IsGuest.Should().BeTrue();
        copy.Visitors.Should().HaveCount(2);
        copy.Visitors[0].Should().BeEquivalentTo(new { PlayerId = listed, Nickname = "Lan Anh", Listed = true, Present = false });
        copy.Visitors[1].Should().BeEquivalentTo(new { PlayerId = coop, Nickname = "guest-0042", Listed = false, Present = true });
    }

    [Fact]
    public void Requests_RoundTrip()
    {
        Guid player = Guid.CreateVersion7();
        using AddVisitor add = new() { Nickname = "Hoàng Yến" };
        using RemoveVisitor remove = new() { PlayerId = player };
        using KickVisitor kick = new() { PlayerId = player };
        using SetCoopVisits coop = new() { Allowed = true };
        using VisitPlayer visit = new() { Nickname = "Minh" };
        using VisitResult result = new() { Status = VisitStatus.NotOnPublicMap };

        AddVisitor.Deserialize(add.Serialize()).Nickname.Should().Be("Hoàng Yến");
        RemoveVisitor.Deserialize(remove.Serialize()).PlayerId.Should().Be(player);
        KickVisitor.Deserialize(kick.Serialize()).PlayerId.Should().Be(player);
        SetCoopVisits.Deserialize(coop.Serialize()).Allowed.Should().BeTrue();
        VisitPlayer.Deserialize(visit.Serialize()).Nickname.Should().Be("Minh");
        VisitResult.Deserialize(result.Serialize()).Status.Should().Be(VisitStatus.NotOnPublicMap);
    }
}
