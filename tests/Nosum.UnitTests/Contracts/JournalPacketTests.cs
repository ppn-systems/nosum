// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Contracts.Journal;
using Nosum.Contracts.Journal.C2S;
using Nosum.Contracts.Journal.S2C;
using Xunit;

namespace Nosum.UnitTests.Contracts;

public sealed class JournalPacketTests
{
    [Fact]
    public void JournalOpCodes_AreFrozenInTheJournalBand()
    {
        // Append-only: these numbers never change and are never reused.
        JournalOpCode.GetJournal.Should().Be(0xD100);
        JournalOpCode.JournalReply.Should().Be(0xD140);
    }

    [Fact]
    public void StaticOpCodes_OfTheJournalPackets_MatchTheirConstants()
    {
        GetJournal.StaticOpCode.Should().Be(JournalOpCode.GetJournal);
        JournalReply.StaticOpCode.Should().Be(JournalOpCode.JournalReply);
    }

    [Fact]
    public void JournalReply_WithEntries_RoundTrips()
    {
        using JournalReply reply = new();
        JournalEntry parsnip = JournalEntry.Create();
        parsnip.ItemId = 200;
        parsnip.Count = 12;
        parsnip.FirstObtainedUnix = 1_790_000_000L;
        reply.Entries.Add(parsnip);

        JournalReply copy = JournalReply.Deserialize(reply.Serialize());

        copy.Entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ItemId = 200, Count = 12, FirstObtainedUnix = 1_790_000_000L });
    }

    [Fact]
    public void JournalReply_Empty_RoundTrips()
    {
        using JournalReply reply = new();

        JournalReply.Deserialize(reply.Serialize()).Entries.Should().BeEmpty();
    }
}
