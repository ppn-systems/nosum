// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Calendar;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Calendar;

/// <summary>
/// The banner across the top of the screen that announces a new season (docs/adr/ADR-062 for the banner pattern). It follows
/// <see cref="CalendarViewModel.SeasonChanged"/>, so a player busy in the fields hears and sees the turn of the year.
/// </summary>
public sealed class SeasonBannerViewModel : IObservableViewModel, IDisposable
{
    /// <summary>How long the banner stays on screen.</summary>
    public static readonly TimeSpan BannerDuration = TimeSpan.FromSeconds(4.5);

    private readonly CalendarViewModel _calendar;
    private readonly IStringLocalizer<Language> _text;
    private int _version;

    /// <summary>Initializes the view model and starts following the season.</summary>
    /// <param name="calendar">The clock and calendar widget, which finds the season change.</param>
    /// <param name="text">UI text.</param>
    public SeasonBannerViewModel(CalendarViewModel calendar, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(text);

        _calendar = calendar;
        _text = text;
        calendar.SeasonChanged += this.Show;
    }

    /// <summary>Raised when <see cref="Title"/> or <see cref="Message"/> changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Gets the banner heading, or <see langword="null"/> when no banner is shown.</summary>
    public string? Title { get; private set; }

    /// <summary>Gets the line naming the new season, or <see langword="null"/> when no banner is shown.</summary>
    public string? Message { get; private set; }

    /// <inheritdoc/>
    public void Dispose() => _calendar.SeasonChanged -= this.Show;

    /// <summary>Returns the text of the banner for a season: the heading and the line that names the season.</summary>
    /// <param name="season">The season that began.</param>
    /// <returns>The heading and the message, localized.</returns>
    public (string Title, string Message) Compose(Season season)
    {
        string name = _text["Season_" + season].Value;
        return (_text["Season_Banner_Title"].Value, string.Format(CultureInfo.CurrentCulture, _text["Season_Banner_Message"].Value, name));
    }

    private void Show(Season season)
    {
        int version = ++_version;
        (this.Title, this.Message) = this.Compose(season);
        this.Changed?.Invoke();
        _ = this.ClearLaterAsync(version);
    }

    private async Task ClearLaterAsync(int version)
    {
        await Task.Delay(BannerDuration).ConfigureAwait(false);
        if (version == _version)
        {
            this.Title = null;
            this.Message = null;
            this.Changed?.Invoke();
        }
    }
}
