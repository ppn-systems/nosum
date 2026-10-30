// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
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

/// <summary>The season banner announces the turn of the season found by the calendar widget, and only the turn: not the first read, and not a reload of the same season.</summary>
public sealed class SeasonBannerViewModelTests
{
    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    [Fact]
    public void Compose_InEnglish_NamesTheNewSeason()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        try
        {
            using CalendarViewModel widget = new(new CalendarChannel(new FakeGameSocket()), Localizer<Language>());
            using SeasonBannerViewModel banner = new(widget, Localizer<Language>());

            (string title, string message) = banner.Compose(Season.Summer);

            title.Should().Be("NEW SEASON");
            message.Should().Be("Summer has arrived!");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Compose_InVietnamese_NamesTheNewSeason()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("vi");
        try
        {
            using CalendarViewModel widget = new(new CalendarChannel(new FakeGameSocket()), Localizer<Language>());
            using SeasonBannerViewModel banner = new(widget, Localizer<Language>());

            (string title, string message) = banner.Compose(Season.Winter);

            title.Should().Be("MÙA MỚI");
            message.Should().Be("Mùa Đông đã đến!");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void SeasonTurn_RaisesTheBannerOnce_AndNotOnTheFirstRead()
    {
        GameCalendar calendar = GameCalendar.Default;
        long springNow = calendar.EpochMs + calendar.DayLengthMs;
        long summerNow = calendar.EpochMs + calendar.SeasonLengthMs + calendar.DayLengthMs;
        long serverNow = springNow;
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new CalendarInfo { EpochMs = calendar.EpochMs, DayLengthMs = calendar.DayLengthMs, ServerNowMs = serverNow });
        CalendarChannel channel = new(socket);
        using CalendarViewModel widget = new(channel, Localizer<Language>());
        using SeasonBannerViewModel banner = new(widget, Localizer<Language>());
        List<Season> turns = [];
        widget.SeasonChanged += turns.Add;

        channel.Load();
        turns.Should().BeEmpty();
        banner.Message.Should().BeNull();

        serverNow = summerNow;
        channel.Load();

        turns.Should().Equal(Season.Summer);
        banner.Message.Should().NotBeNullOrEmpty();
    }
}
