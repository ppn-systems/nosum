// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Nosum.Wasm.Socket.Coop;
using Nosum.Wasm.ViewModels.Coop;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The "who contributed" totals of the co-op page: the co-op's value shows per member, from the warehouse log.</summary>
public sealed class CoopContributionsTests
{
    private static readonly Guid s_ann = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid s_ben = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid s_cam = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    [Fact]
    public void Summarize_CountsDepositsPerMemberBiggestContributorFirst()
    {
        List<CoopLogInfo> log =
        [
            new(s_ben, 1, 3, 1),
            new(s_ann, 2, 5, 2),
            new(s_ben, 2, 4, 3),
        ];

        CoopContributions.Summarize(log).Should().Equal(new CoopContributionTotal(s_ben, 7), new CoopContributionTotal(s_ann, 5));
    }

    [Fact]
    public void Summarize_LeavesOutWithdrawalsAndUpgradeCosts()
    {
        // A negative delta is stock taken out: a withdrawal, or what an upgrade paid. Neither is a gift to the co-op.
        List<CoopLogInfo> log =
        [
            new(s_ann, 1, 4, 1),
            new(s_ann, 1, -10, 2),
            new(s_ben, 1, -2, 3),
        ];

        CoopContributions.Summarize(log).Should().Equal(new CoopContributionTotal(s_ann, 4));
    }

    [Fact]
    public void Summarize_EqualTotals_AreOrderedByMemberIdSoTheListDoesNotShuffle()
    {
        List<CoopLogInfo> log = [new(s_cam, 1, 2, 1), new(s_ann, 1, 2, 2)];

        CoopContributions.Summarize(log).Should().Equal(new CoopContributionTotal(s_ann, 2), new CoopContributionTotal(s_cam, 2));
    }

    [Fact]
    public void Summarize_FormerMemberKeepsTheEmptyId()
    {
        List<CoopLogInfo> log = [new(Guid.Empty, 1, 6, 1), new(s_ann, 1, 1, 2)];

        CoopContributions.Summarize(log).Should().Equal(new CoopContributionTotal(Guid.Empty, 6), new CoopContributionTotal(s_ann, 1));
    }

    [Fact]
    public void Summarize_EmptyLog_IsEmpty()
    {
        CoopContributions.Summarize([]).Should().BeEmpty();
    }

    [Fact]
    public void Language_ContributionSectionHasTextInBothLanguages()
    {
        string[] needed = ["Coop_Contrib_Row", "Coop_Contrib_Window"];

        SocialClientFixtures.Keys("Language.resx").Should().Contain(needed);
        SocialClientFixtures.Keys("Language.en.resx").Should().Contain(needed);
    }

    [Fact]
    public void Summarize_NullLog_Throws()
    {
        Action call = () => CoopContributions.Summarize(null!);

        call.Should().Throw<ArgumentNullException>();
    }
}
