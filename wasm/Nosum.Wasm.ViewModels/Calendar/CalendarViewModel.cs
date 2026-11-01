// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Weather;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Calendar;

/// <summary>
/// View model of the clock and calendar widget: season, day of season and time of day, computed from the server calendar with the same
/// <see cref="GameCalendar"/> the server uses (docs/adr/ADR-018). The time shows every game minute (30 real seconds in the default calendar) and
/// the widget is re-read every second, so the clock visibly advances; it re-renders only when the text changes.
/// </summary>
public sealed class CalendarViewModel : PanelViewModel, IDisposable
{
    /// <summary>How often the widget reads the clock, real seconds: a game minute is 30 real seconds, so the change shows within a second.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private const int TimeStepMinutes = 1;

    private readonly CalendarChannel _channel;
    private readonly IStringLocalizer<Catalogue>? _catalogue;
    private readonly Timer _tick;
    private GameCalendar? _calendar;
    private Season? _season;
    private string _shown = string.Empty;

    /// <summary>
    /// Raised when the season turns on the server calendar, with the new season. Found by the same one-second refresh as the widget, so it can be up
    /// to one real second late. Not raised for the first season read.
    /// </summary>
    public event Action<Season>? SeasonChanged;

    /// <summary>Initializes the view model.</summary>
    /// <param name="channel">The server calendar.</param>
    /// <param name="text">UI text.</param>
    /// <param name="catalogue">Item and crop names, for the Today card's list of crops in season; without it the list is empty.</param>
    public CalendarViewModel(CalendarChannel channel, IStringLocalizer<Language> text, IStringLocalizer<Catalogue>? catalogue = null)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(channel);

        _channel = channel;
        _catalogue = catalogue;
        channel.Changed += this.OnLoaded;
        _tick = new Timer(_ => this.Refresh(), null, RefreshInterval, RefreshInterval);
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

    /// <summary>Gets the time of day in the current culture's short time format, to the game minute.</summary>
    public string TimeText
    {
        get
        {
            int minute = this.Now().MinuteOfDay / TimeStepMinutes * TimeStepMinutes;
            return new DateTime(2000, 1, 1, minute / 60, minute % 60, 0, DateTimeKind.Unspecified).ToString("t", CultureInfo.CurrentCulture);
        }
    }

    /// <summary>Gets today's weather, computed from the calendar like the server does (docs/adr/ADR-027); sun until the calendar is known.</summary>
    public WeatherKind Today => _calendar is { } calendar ? WeatherRules.At(calendar, _channel.ServerNowMs()) : WeatherKind.Sun;

    /// <summary>Gets tomorrow's forecast: the same function for the next game day (docs/adr/ADR-027 D3).</summary>
    public WeatherKind Tomorrow => _calendar is { } calendar ? WeatherRules.ForDay(calendar, calendar.DayNumberAt(_channel.ServerNowMs()) + 1) : WeatherKind.Sun;

    /// <summary>Gets the CSS name of today's weather icon: <c>sun</c>, <c>rain</c>, <c>storm</c> or <c>snow</c>.</summary>
    public string TodayKey => WeatherKey(this.Today);

    /// <summary>Gets the CSS name of tomorrow's weather icon.</summary>
    public string TomorrowKey => WeatherKey(this.Tomorrow);

    /// <summary>Gets the localized name of today's weather.</summary>
    public string TodayName => this.Text("Weather_" + this.Today);

    /// <summary>Gets the localized forecast phrase for a screen reader and a tooltip, for example <c>Today: rain. Tomorrow: sun.</c></summary>
    public string ForecastText => this.Text("Calendar_Forecast", this.TodayName, this.Text("Weather_" + this.Tomorrow));

    /// <summary>Gets the localized name of tomorrow's weather.</summary>
    public string TomorrowName => this.Text("Weather_" + this.Tomorrow);

    /// <summary>Gets the Today card's date line: the season and the day of the season, for example <c>Spring, Day 12</c>.</summary>
    public string DateText => this.Text("Today_Date", this.SeasonName, this.DayText);

    /// <summary>Gets the tooltip of the calendar bar, which opens the Today card.</summary>
    public string TodayTip => this.Text("Today_Tip");

    /// <summary>
    /// Gets the names of the crops that grow in the current season, in catalogue order, from the harvest item's localized name (backlog item 7).
    /// Empty until the calendar is known and a catalogue is given.
    /// </summary>
    public IReadOnlyList<string> CropsInSeason
    {
        get
        {
            List<string> names = [];
            if (_catalogue is null || !this.IsReady)
            {
                return names;
            }

            Season season = this.Season;
            foreach (CropDefinition crop in CropCatalog.All)
            {
                if (crop.GrowsIn(season) && ItemCatalog.TryGet(crop.HarvestItemId, out ItemDefinition? harvest))
                {
                    names.Add(_catalogue["Item_" + harvest.Key + "_Name"].Value);
                }
            }

            return names;
        }
    }

    /// <summary>Gets the Today card's crop line: the crops in season, comma separated, or a line saying there are none.</summary>
    public string CropsText
    {
        get
        {
            IReadOnlyList<string> names = this.CropsInSeason;
            return names.Count == 0 ? this.Text("Today_Crops_None") : this.Text("Today_Crops", string.Join(", ", names));
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

    private static string WeatherKey(WeatherKind weather) => weather.ToString().ToLowerInvariant();

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

        Season season = this.Season;
        if (_season is { } previous && season != previous)
        {
            this.SeasonChanged?.Invoke(season);
        }

        _season = season;
        string now = this.Summary + "|" + this.ForecastText;
        if (string.Equals(now, _shown, StringComparison.Ordinal))
        {
            return;
        }

        _shown = now;
        this.Raise();
    }
}
