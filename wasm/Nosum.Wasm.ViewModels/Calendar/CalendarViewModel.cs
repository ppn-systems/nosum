// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Calendar;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Calendar;

/// <summary>
/// View model of the clock and calendar widget: season, day of season and time of day, computed from the server calendar with the same
/// <see cref="GameCalendar"/> the server uses (docs/adr/ADR-018). The time shows in ten game-minute steps (five real minutes in the default
/// calendar), so a slow timer is enough and the widget re-renders only when the text changes.
/// </summary>
public sealed class CalendarViewModel : PanelViewModel, IDisposable
{
    private const int TimeStepMinutes = 10;

    private readonly CalendarChannel _channel;
    private readonly Timer _tick;
    private GameCalendar? _calendar;
    private string _shown = string.Empty;

    /// <summary>Initializes the view model.</summary>
    /// <param name="channel">The server calendar.</param>
    /// <param name="text">UI text.</param>
    public CalendarViewModel(CalendarChannel channel, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(channel);

        _channel = channel;
        channel.Changed += this.OnLoaded;
        _tick = new Timer(_ => this.Refresh(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>Gets a value indicating whether the calendar is known; the widget shows nothing before.</summary>
    public bool IsReady => _calendar is not null;

    /// <summary>Gets the current season, spring until the calendar is known.</summary>
    public Season Season => this.Now().Season;

    /// <summary>Gets the CSS name of the season icon: <c>spring</c>, <c>summer</c>, <c>fall</c> or <c>winter</c>.</summary>
    public string SeasonKey => this.Season.ToString().ToLowerInvariant();

    /// <summary>Gets the localized season name.</summary>
    public string SeasonName => this.Text("Season_" + this.Season);

    /// <summary>Gets the localized day of the season, for example <c>Day 12</c>.</summary>
    public string DayText => this.Text("Calendar_Day", this.Now().DayOfSeason);

    /// <summary>Gets the localized year, for example <c>Year 1</c>.</summary>
    public string YearText => this.Text("Calendar_Year", this.Now().Year);

    /// <summary>Gets the time of day in the current culture's short time format, rounded down to ten minutes.</summary>
    public string TimeText
    {
        get
        {
            int minute = this.Now().MinuteOfDay / TimeStepMinutes * TimeStepMinutes;
            return new DateTime(2000, 1, 1, minute / 60, minute % 60, 0, DateTimeKind.Unspecified).ToString("t", CultureInfo.CurrentCulture);
        }
    }

    /// <summary>Gets the whole widget as one phrase for a screen reader.</summary>
    public string Summary => this.Text("Calendar_Summary", this.SeasonName, this.DayText, this.TimeText, this.YearText);

    /// <inheritdoc/>
    public void Dispose()
    {
        _channel.Changed -= this.OnLoaded;
        _tick.Dispose();
    }

    private GameDate Now() => _calendar is { } calendar ? calendar.At(_channel.ServerNowMs()) : default;

    private void OnLoaded()
    {
        _calendar = new GameCalendar(_channel.EpochMs, _channel.DayLengthMs);
        this.Refresh();
    }

    private void Refresh()
    {
        if (_calendar is null)
        {
            return;
        }

        string now = this.Summary;
        if (string.Equals(now, _shown, StringComparison.Ordinal))
        {
            return;
        }

        _shown = now;
        this.Raise();
    }
}
