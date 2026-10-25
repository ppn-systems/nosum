// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using System.Reflection;
using FluentAssertions;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Requests;
using Nosum.Contracts.Requests.C2S;
using Nosum.Contracts.Requests.S2C;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>The requests board packets (docs/adr/ADR-036 D8): frozen opcodes inside their slice of the Quest band, append-only statuses and a full round trip.</summary>
public sealed class RequestPacketTests
{
    [Fact]
    public void RequestsOpCode_Values_AreFrozenInTheirSliceOfTheQuestBand()
    {
        RequestsOpCode.GetRequests.Should().Be(0x6100);
        RequestsOpCode.DeliverRequest.Should().Be(0x6101);
        RequestsOpCode.RequestsSnapshot.Should().Be(0x6140);
        RequestsOpCode.RequestResult.Should().Be(0x6141);

        ushort[] values = [.. typeof(RequestsOpCode).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (ushort)f.GetRawConstantValue()!)];
        values.Should().OnlyHaveUniqueItems().And.OnlyContain(v => v >= 0x6100 && v <= 0x61FF);
        QuestOpCode.QuestUpdate.Should().Be(0x6041, "the quest opcodes did not move");
    }

    [Fact]
    public void StaticOpCodes_OfEveryRequestPacket_MatchTheirConstants()
    {
        GetRequests.StaticOpCode.Should().Be(RequestsOpCode.GetRequests);
        DeliverRequest.StaticOpCode.Should().Be(RequestsOpCode.DeliverRequest);
        RequestsSnapshot.StaticOpCode.Should().Be(RequestsOpCode.RequestsSnapshot);
        RequestResult.StaticOpCode.Should().Be(RequestsOpCode.RequestResult);
    }

    [Fact]
    public void RequestStatus_IsAppendOnly()
    {
        ((byte)RequestStatus.Ok).Should().Be(0);
        ((byte)RequestStatus.UnknownRequest).Should().Be(1);
        ((byte)RequestStatus.NotToday).Should().Be(2);
        ((byte)RequestStatus.AlreadyFilled).Should().Be(3);
        ((byte)RequestStatus.NotAtBoard).Should().Be(4);
        ((byte)RequestStatus.NotEnough).Should().Be(5);
        ((byte)RequestStatus.PurseFull).Should().Be(6);
        ((byte)RequestStatus.Conflict).Should().Be(7);
    }

    [Fact]
    public void DeliverRequest_RoundTrips()
    {
        using DeliverRequest packet = new() { Day = 1_234, Slot = 2 };

        DeliverRequest copy = DeliverRequest.Deserialize(packet.Serialize());

        copy.Day.Should().Be(1_234);
        copy.Slot.Should().Be(2);
    }

    [Fact]
    public void RequestsSnapshot_RoundTripsWithEveryRow()
    {
        using RequestsSnapshot packet = new() { ServerNowMs = 1_700_000_000_000, Day = 99, DayEndsAtMs = 1_700_043_200_000 };
        packet.Rows.Add(new RequestRow { Slot = 0, ItemId = 200, Quantity = 5, RewardCoins = 130, Filled = false });
        packet.Rows.Add(new RequestRow { Slot = 1, ItemId = 201, Quantity = 3, RewardCoins = 117, Filled = true });

        RequestsSnapshot copy = RequestsSnapshot.Deserialize(packet.Serialize());

        copy.ServerNowMs.Should().Be(1_700_000_000_000);
        copy.Day.Should().Be(99);
        copy.DayEndsAtMs.Should().Be(1_700_043_200_000);
        copy.Rows.Should().BeEquivalentTo(packet.Rows, o => o.WithStrictOrdering());
    }

    [Fact]
    public void RequestResult_RoundTrips()
    {
        using RequestResult packet = new() { Status = RequestStatus.AlreadyFilled, Reward = 0 };
        RequestResult.Deserialize(packet.Serialize()).Status.Should().Be(RequestStatus.AlreadyFilled);

        using RequestResult paid = new() { Status = RequestStatus.Ok, Reward = 130 };
        RequestResult copy = RequestResult.Deserialize(paid.Serialize());
        (copy.Status, copy.Reward).Should().Be((RequestStatus.Ok, 130L));

        using GetRequests request = new();
        GetRequests.Deserialize(request.Serialize()).Should().NotBeNull();
    }
}
