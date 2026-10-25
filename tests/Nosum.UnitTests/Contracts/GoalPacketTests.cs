// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using System.Reflection;
using FluentAssertions;
using Nosum.Contracts.Goals;
using Nosum.Contracts.Goals.C2S;
using Nosum.Contracts.Goals.S2C;
using Nosum.Contracts.Market;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Market.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.World.S2C;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>The community goal packets and the price history packets: frozen opcodes (docs/adr/ADR-030 D5), append-only statuses and a full round trip.</summary>
public sealed class GoalPacketTests
{
    [Fact]
    public void GoalOpCode_Values_AreFrozenInTheirSliceOfTheMarketBand()
    {
        GoalOpCode.GetGoals.Should().Be(0x4100);
        GoalOpCode.DeliverToGoal.Should().Be(0x4101);
        GoalOpCode.GoalsSnapshot.Should().Be(0x4140);
        GoalOpCode.DeliverResult.Should().Be(0x4141);
        GoalOpCode.GoalProgress.Should().Be(0x4142);
        GoalOpCode.UnlocksChanged.Should().Be(0x4143);

        ushort[] values = [.. typeof(GoalOpCode).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (ushort)f.GetRawConstantValue()!)];
        values.Should().OnlyHaveUniqueItems().And.OnlyContain(v => v >= 0x4100 && v <= 0x41FF);
    }

    [Fact]
    public void MarketOpCode_PriceHistory_IsAppendedInsideTheMarketSlice_AndNothingMoved()
    {
        MarketOpCode.GetMyListings.Should().Be(0x4006);
        MarketOpCode.MarketResult.Should().Be(0x4044);
        MarketOpCode.GetPriceHistory.Should().Be(0x4007);
        MarketOpCode.PriceHistory.Should().Be(0x4045);

        ushort[] values = [.. typeof(MarketOpCode).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (ushort)f.GetRawConstantValue()!)];
        values.Should().OnlyHaveUniqueItems().And.OnlyContain(v => v >= 0x4000 && v <= 0x40FF);
    }

    [Fact]
    public void StaticOpCodes_OfEveryGoalAndHistoryPacket_MatchTheirConstants()
    {
        GetGoals.StaticOpCode.Should().Be(GoalOpCode.GetGoals);
        DeliverToGoal.StaticOpCode.Should().Be(GoalOpCode.DeliverToGoal);
        GoalsSnapshot.StaticOpCode.Should().Be(GoalOpCode.GoalsSnapshot);
        DeliverResult.StaticOpCode.Should().Be(GoalOpCode.DeliverResult);
        GoalProgress.StaticOpCode.Should().Be(GoalOpCode.GoalProgress);
        GetPriceHistory.StaticOpCode.Should().Be(MarketOpCode.GetPriceHistory);
        PriceHistory.StaticOpCode.Should().Be(MarketOpCode.PriceHistory);
    }

    [Fact]
    public void Statuses_AreAppendOnly()
    {
        ((byte)GoalStatus.Ok).Should().Be(0);
        ((byte)GoalStatus.UnknownGoal).Should().Be(1);
        ((byte)GoalStatus.InvalidQuantity).Should().Be(2);
        ((byte)GoalStatus.NotThisSeason).Should().Be(3);
        ((byte)GoalStatus.Closed).Should().Be(4);
        ((byte)GoalStatus.NotAtBoard).Should().Be(5);
        ((byte)GoalStatus.NotEnough).Should().Be(6);
        ((byte)GoalStatus.Conflict).Should().Be(7);
        ((byte)GoalState.Active).Should().Be(0);
        ((byte)GoalState.Completed).Should().Be(1);
        ((byte)GoalState.Expired).Should().Be(2);
        ((byte)MarketStatus.NotYourBin).Should().Be(18);
        ((byte)MarketStatus.TooManyOfItem).Should().Be(19);
    }

    [Fact]
    public void DeliverToGoal_RoundTrips()
    {
        using DeliverToGoal packet = new() { GoalId = 3, Quantity = 250 };

        DeliverToGoal copy = DeliverToGoal.Deserialize(packet.Serialize());

        copy.GoalId.Should().Be(3);
        copy.Quantity.Should().Be(250);
    }

    [Fact]
    public void GoalsSnapshot_RoundTripsWithEveryRow()
    {
        using GoalsSnapshot packet = new() { ServerNowMs = 1_700_000_000_000, SeasonEndsAtMs = 1_701_000_000_000 };
        packet.Rows.Add(new GoalRow { GoalId = 1, SeasonNumber = 4, Total = 4_321, State = GoalState.Active, Mine = 120, Contributors = 17 });
        packet.Rows.Add(new GoalRow { GoalId = 2, SeasonNumber = 1, Total = 1_500, State = GoalState.Completed, Mine = 0, Contributors = 40 });

        GoalsSnapshot copy = GoalsSnapshot.Deserialize(packet.Serialize());

        copy.ServerNowMs.Should().Be(1_700_000_000_000);
        copy.SeasonEndsAtMs.Should().Be(1_701_000_000_000);
        copy.Rows.Should().HaveCount(2);
        copy.Rows[0].Should().BeEquivalentTo(new GoalRow { GoalId = 1, SeasonNumber = 4, Total = 4_321, State = GoalState.Active, Mine = 120, Contributors = 17 });
        copy.Rows[1].State.Should().Be(GoalState.Completed);
    }

    [Fact]
    public void DeliverResultAndGoalProgress_RoundTrip()
    {
        using DeliverResult result = new() { Status = GoalStatus.NotAtBoard, Delivered = 0 };
        DeliverResult.Deserialize(result.Serialize()).Status.Should().Be(GoalStatus.NotAtBoard);

        using GoalProgress progress = new() { GoalId = 4, SeasonNumber = 7, Total = 999, State = GoalState.Expired, Contributors = 3 };
        GoalProgress copy = GoalProgress.Deserialize(progress.Serialize());
        copy.GoalId.Should().Be(4);
        copy.SeasonNumber.Should().Be(7);
        copy.Total.Should().Be(999);
        copy.State.Should().Be(GoalState.Expired);
        copy.Contributors.Should().Be(3);
    }

    [Fact]
    public void Unlocks_TravelWithTheJoinTheMapChangeAndThePush_AppendedAfterTheOldFields()
    {
        UnlocksChanged.StaticOpCode.Should().Be(GoalOpCode.UnlocksChanged);
        using UnlocksChanged push = new() { Unlocks = 1 };
        UnlocksChanged.Deserialize(push.Serialize()).Unlocks.Should().Be(1);

        using JoinMapResult join = new() { Status = JoinStatus.Ok, PlayerId = 4, SpawnX = 1.5f, SpawnY = 2.5f, MapId = "beach", Facing = 2, Unlocks = 1 };
        JoinMapResult joined = JoinMapResult.Deserialize(join.Serialize());
        joined.MapId.Should().Be("beach");
        joined.Facing.Should().Be(2);
        joined.Unlocks.Should().Be(1);

        using MapChanged change = new() { PlayerId = 4, MapId = "beach", X = 3, Y = 4, Facing = 1, Unlocks = 1 };
        MapChanged changed = MapChanged.Deserialize(change.Serialize());
        changed.Facing.Should().Be(1);
        changed.Unlocks.Should().Be(1);

        typeof(JoinMapResult).GetProperty(nameof(JoinMapResult.Unlocks))!.GetCustomAttribute<Nalix.Abstractions.Serialization.SerializeOrderAttribute>()!.Order.Should().Be(6);
        typeof(MapChanged).GetProperty(nameof(MapChanged.Unlocks))!.GetCustomAttribute<Nalix.Abstractions.Serialization.SerializeOrderAttribute>()!.Order.Should().Be(5);
    }

    [Fact]
    public void PriceHistory_RoundTripsEveryDay()
    {
        using PriceHistory packet = new() { ItemId = 101, Today = 80 };
        packet.Days.Add(new PriceDayRow { Day = 78, Min = 9, Median = 10, Max = 12, Sales = 3 });
        packet.Days.Add(new PriceDayRow { Day = 80, Min = 11, Median = 11, Max = 11, Sales = 1 });

        PriceHistory copy = PriceHistory.Deserialize(packet.Serialize());

        copy.ItemId.Should().Be(101);
        copy.Today.Should().Be(80);
        copy.Days.Should().BeEquivalentTo(packet.Days);

        using GetPriceHistory request = new() { ItemId = 101 };
        GetPriceHistory.Deserialize(request.Serialize()).ItemId.Should().Be(101);
    }
}
