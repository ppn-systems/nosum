// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Abstractions.Accounts;
using Nosum.Core;
using Nosum.Infrastructure.Players;
using Xunit;

namespace Nosum.UnitTests.Server;

public sealed class RecentPlacesTests
{
    private static readonly Guid Account = Guid.Parse("6f1c2a64-9d3e-4b7a-8e21-0c5d7b9a4f10");
    private static readonly PlayerStateRow Field = new("farm", 120.5, 64.0, Facing.Down);

    [Fact]
    public void TryTake_ReconnectWithinTheWindow_ReturnsTheLeftPlace()
    {
        ManualClock clock = new();
        RecentPlaces places = new(clock);
        places.Remember(Account, Field);
        clock.Advance(TimeSpan.FromSeconds(59));

        places.TryTake(Account, out PlayerStateRow place).Should().BeTrue();
        place.Should().Be(Field);
    }

    [Fact]
    public void TryTake_AfterTheWindow_ReturnsFalse()
    {
        ManualClock clock = new();
        RecentPlaces places = new(clock);
        places.Remember(Account, Field);
        clock.Advance(RecentPlaces.Window);

        places.TryTake(Account, out _).Should().BeFalse();
    }

    [Fact]
    public void TryTake_Twice_ReturnsTheRecordOnlyOnce()
    {
        ManualClock clock = new();
        RecentPlaces places = new(clock);
        places.Remember(Account, Field);

        places.TryTake(Account, out _).Should().BeTrue();
        places.TryTake(Account, out _).Should().BeFalse();
    }

    [Fact]
    public void Remember_LeftAgain_KeepsTheLatestPlaceAndItsTime()
    {
        ManualClock clock = new();
        RecentPlaces places = new(clock);
        places.Remember(Account, Field);
        clock.Advance(TimeSpan.FromSeconds(50));

        PlayerStateRow later = Field with { X = 300.0 };
        places.Remember(Account, later);
        clock.Advance(TimeSpan.FromSeconds(50));

        places.TryTake(Account, out PlayerStateRow place).Should().BeTrue();
        place.Should().Be(later);
    }

    [Fact]
    public void TryTake_UnknownAccount_ReturnsFalse()
    {
        RecentPlaces places = new(new ManualClock());

        places.TryTake(Account, out _).Should().BeFalse();
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
