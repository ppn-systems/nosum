// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Season.S2C;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.ViewModels.Calendar;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The Today card's data (docs/design/research-hud-and-loops.md, backlog item 7): the crops that grow this season, from the server calendar.</summary>
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the channels live as long as the test.")]
public sealed class CalendarTodayCardTests
{
    [Fact]
    public void CropsInSeason_AreTheCropsThatGrowInTheCurrentSeason()
    {
        using CultureScope scope = new("en");
        CalendarViewModel vm = Loaded(GameCalendar.Default.EpochMs + (GameCalendar.Default.DayLengthMs / 2));

        int expected = CropCatalog.All.Count(c => c.GrowsIn(Season.Spring));

        vm.Season.Should().Be(Season.Spring);
        vm.CropsInSeason.Should().HaveCount(expected).And.NotContainNulls();
        vm.CropsInSeason.Should().NotBeEmpty("spring has crops");
    }

    [Fact]
    public void CropsInSeason_ChangeWithTheSeason()
    {
        using CultureScope scope = new("en");
        GameCalendar calendar = GameCalendar.Default;
        CalendarViewModel vm = Loaded(calendar.EpochMs + calendar.SeasonLengthMs + (calendar.DayLengthMs / 2));

        vm.Season.Should().Be(Season.Summer);
        vm.CropsInSeason.Should().HaveCount(CropCatalog.All.Count(c => c.GrowsIn(Season.Summer)));
    }

    [Fact]
    public void CropsInSeason_WithoutACatalogue_IsEmpty_AndTheLineSaysSo()
    {
        using CultureScope scope = new("en");
        CalendarViewModel vm = Loaded(GameCalendar.Default.EpochMs + (GameCalendar.Default.DayLengthMs / 2), catalogue: false);

        vm.CropsInSeason.Should().BeEmpty();
        vm.CropsText.Should().Be("No crops grow this season");
    }

    [Fact]
    public void CropsInSeason_BeforeTheCalendarIsKnown_IsEmpty()
    {
        using CultureScope scope = new("en");
        FakeGameSocket socket = new();
        CalendarChannel channel = new(socket);
        using CalendarViewModel vm = new(channel, Localizer<Language>(), Localizer<Catalogue>());

        vm.CropsInSeason.Should().BeEmpty();
    }

    [Fact]
    public void DateAndTomorrow_ReadTheSeasonDayAndTheForecast()
    {
        using CultureScope scope = new("en");
        CalendarViewModel vm = Loaded(GameCalendar.Default.EpochMs + (GameCalendar.Default.DayLengthMs / 2));

        vm.DateText.Should().StartWith("Spring, ");
        vm.TomorrowName.Should().NotBeNullOrWhiteSpace();
    }

    // The view model subscribes before the channel loads, as the widget does; the server clock is the given time.
    private static CalendarViewModel Loaded(long serverNow, bool catalogue = true)
    {
        GameCalendar calendar = GameCalendar.Default;
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new CalendarInfo { EpochMs = calendar.EpochMs, DayLengthMs = calendar.DayLengthMs, ServerNowMs = serverNow });
        CalendarChannel channel = new(socket);
        CalendarViewModel vm = new(channel, Localizer<Language>(), catalogue ? Localizer<Catalogue>() : null);
        channel.Load();
        return vm;
    }

    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    private sealed class CultureScope : System.IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

        public CultureScope(string name)
        {
            CultureInfo.CurrentUICulture = new CultureInfo(name);
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
        }

        public void Dispose()
        {
            CultureInfo.CurrentUICulture = _previous;
            CultureInfo.CurrentCulture = _previousCulture;
        }
    }
}
