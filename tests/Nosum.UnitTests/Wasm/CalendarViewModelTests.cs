// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Season.S2C;
using Nosum.Gameplay.Calendar;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.ViewModels.Calendar;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The clock widget computes season, day and time locally from the epoch and day length the server sent (docs/adr/ADR-018).</summary>
public sealed class CalendarViewModelTests
{
    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    [Fact]
    public void Widget_BeforeTheCalendarIsKnown_IsNotReady()
    {
        FakeGameSocket socket = new();
        CalendarChannel channel = new(socket);
        using CalendarViewModel vm = new(channel, Localizer<Language>());

        vm.IsReady.Should().BeFalse();
        channel.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void Widget_AfterLoad_ShowsSeasonDayAndTimeInTheCurrentLanguage()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try
        {
            GameCalendar calendar = GameCalendar.Default;
            // The server clock is two seasons and nine and a half days past the epoch.
            long serverNow = calendar.EpochMs + (2 * calendar.SeasonLengthMs) + (9 * calendar.DayLengthMs) + (calendar.DayLengthMs / 2);
            FakeGameSocket socket = new();
            socket.ReplyWith(_ => new CalendarInfo { EpochMs = calendar.EpochMs, DayLengthMs = calendar.DayLengthMs, ServerNowMs = serverNow });
            CalendarChannel channel = new(socket);
            using CalendarViewModel vm = new(channel, Localizer<Language>());
            bool raised = false;
            vm.Changed += () => raised = true;

            channel.Load();

            raised.Should().BeTrue();
            vm.IsReady.Should().BeTrue();
            vm.SeasonName.Should().Be("Fall");
            vm.SeasonKey.Should().Be("fall");
            vm.DayText.Should().Be("Day 10");
            vm.YearText.Should().Be("Year 1");
            vm.TimeText.Should().StartWith("12:").And.EndWith("PM");
            (channel.ServerNowMs() - serverNow).Should().BeInRange(-1000, 5000, "the server clock estimate starts at the server's time");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Widget_InVietnamese_NamesTheSeasonInVietnamese()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("vi");
        try
        {
            GameCalendar calendar = GameCalendar.Default;
            FakeGameSocket socket = new();
            socket.ReplyWith(_ => new CalendarInfo { EpochMs = calendar.EpochMs, DayLengthMs = calendar.DayLengthMs, ServerNowMs = calendar.EpochMs + calendar.SeasonLengthMs });
            CalendarChannel channel = new(socket);
            using CalendarViewModel vm = new(channel, Localizer<Language>());

            channel.Load();

            vm.SeasonName.Should().Be("Hè");
            vm.DayText.Should().Be("Ngày 1");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
