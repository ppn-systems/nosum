// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using FluentAssertions;
using Nosum.Contracts.Season.C2S;
using Nosum.Contracts.Season.S2C;
using Nosum.Gameplay.Calendar;
using Nosum.Wasm.Socket.Calendar;
using Xunit;

namespace Nosum.UnitTests.Wasm;

[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing.")]
public sealed class ServerClockTests
{
    [Fact]
    public async Task GetUtcNow_FollowsTheServerClock_WhenTheServerMovesItsClock()
    {
        long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new CalendarInfo { EpochMs = 0, DayLengthMs = GameCalendar.DefaultDayLengthMs, ServerNowMs = serverNow });
        CalendarChannel calendar = new(socket);
        ServerClock clock = new(calendar);

        calendar.Load();
        for (int i = 0; i < 200 && !calendar.IsLoaded; i++)
        {
            await Task.Delay(5);
        }

        // The server's clock is an hour ahead, as after a dev jump; the next read of the calendar moves the estimate with it.
        socket.ReplyWith(_ => new CalendarInfo { EpochMs = 0, DayLengthMs = GameCalendar.DefaultDayLengthMs, ServerNowMs = serverNow + 3_600_000 });
        calendar.Load();
        for (int i = 0; i < 200 && clock.GetUtcNow().ToUnixTimeMilliseconds() < serverNow + 3_600_000; i++)
        {
            await Task.Delay(5);
        }

        long estimate = clock.GetUtcNow().ToUnixTimeMilliseconds();
        estimate.Should().BeGreaterThanOrEqualTo(serverNow + 3_600_000).And.BeLessThan(serverNow + 3_600_000 + 60_000);
    }
}
