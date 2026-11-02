// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Reflection;
using FluentAssertions;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Validation;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>
/// The DevSeason wire range must accept zero: zero is the "to the next Spring" request the QA run starts with (docs/adr/ADR-079 D3). A range of
/// 1 to 4 made the packet invalid on the wire, so the server dropped it and the springReset QA call did nothing (world pass 7).
/// </summary>
public sealed class DevSeasonPacketTests
{
    [Fact]
    public void Seasons_WireRange_AcceptsZeroForTheNextSpring()
    {
        WireRangeAttribute range = typeof(DevSeason).GetProperty(nameof(DevSeason.Seasons))!.GetCustomAttribute<WireRangeAttribute>()!;

        range.Minimum.Should().Be(0);
        range.Maximum.Should().Be(4);
    }
}
