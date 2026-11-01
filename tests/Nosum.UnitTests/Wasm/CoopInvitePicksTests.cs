// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Wasm.ViewModels.Coop;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The co-op invite picker offers the nearby players who are not members yet (critique-social finding 7).</summary>
public sealed class CoopInvitePicksTests
{
    [Fact]
    public void Candidates_MemberOnTheMap_IsNotOfferedAgain()
    {
        List<(uint Id, string Label)> nearby = [(7, "QaFriend4737"), (8, "Guest 0603")];

        IReadOnlyList<(uint Id, string Label)> picks = CoopInvitePicks.Candidates(nearby, ["QaFriend4737"]);

        picks.Select(p => p.Label).Should().Equal("Guest 0603");
    }

    [Fact]
    public void Candidates_NobodyIsAMember_OffersEveryNearbyPlayerInOrder()
    {
        List<(uint Id, string Label)> nearby = [(3, "Guest 1111"), (4, "Guest 2222")];

        IReadOnlyList<(uint Id, string Label)> picks = CoopInvitePicks.Candidates(nearby, []);

        picks.Should().Equal([(3u, "Guest 1111"), (4u, "Guest 2222")]);
    }

    [Fact]
    public void Candidates_EmptyMemberLabel_IsIgnored()
    {
        List<(uint Id, string Label)> nearby = [(5, "Guest 3333")];

        IReadOnlyList<(uint Id, string Label)> picks = CoopInvitePicks.Candidates(nearby, ["", "Guest 9999"]);

        picks.Should().HaveCount(1);
    }
}
