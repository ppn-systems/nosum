// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Calendar;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Placeable;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Feedback;

/// <summary>
/// The morning warning about thirsty crops (docs/adr/ADR-021 D7): when a game day begins, the message line says how many planted crops one more dry
/// day would wither. The day change is looked for once a second; if the farm's tiles have not arrived yet the warning waits for them, so a sign-in
/// on a new day still warns. Nothing is stored and the server decides nothing here.
/// </summary>
public sealed class ThirstWarning : IDisposable
{
    /// <summary>How often the game day is looked at.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    private readonly FarmChannel _farm;
    private readonly PlaceableChannel _placeables;
    private readonly CalendarChannel _calendar;
    private readonly GameMessagesViewModel _messages;
    private readonly IStringLocalizer<Language> _text;
    private readonly Lock _gate = new();
    private readonly Timer _tick;
    private GameCalendar? _rules;
    private int _day;
    private bool _pending;

    /// <summary>Initializes the warning and starts looking at the calendar.</summary>
    /// <param name="farm">The farm channel (the worked tiles).</param>
    /// <param name="placeables">The placeable channel (the sprinklers beside a tile).</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="messages">The message line the warning is shown on.</param>
    /// <param name="text">UI text.</param>
    public ThirstWarning(FarmChannel farm, PlaceableChannel placeables, CalendarChannel calendar, GameMessagesViewModel messages, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(farm);
        ArgumentNullException.ThrowIfNull(placeables);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(text);

        _farm = farm;
        _placeables = placeables;
        _calendar = calendar;
        _messages = messages;
        _text = text;
        farm.Changed += this.Check;
        _tick = new Timer(_ => this.Check(), null, CheckInterval, CheckInterval);
    }

    /// <summary>Stops looking at the calendar and the farm.</summary>
    public void Dispose()
    {
        _farm.Changed -= this.Check;
        _tick.Dispose();
    }

    // Runs on the timer and on every farm change; shows the warning once per game day, when the farm is known.
    private void Check()
    {
        // The gate guards only this object's state; the farm is asked outside it, so the two locks are never held together.
        GameCalendar rules;
        long nowMs;
        lock (_gate)
        {
            if (!_calendar.IsLoaded)
            {
                return;
            }

            rules = this.Rules();
            nowMs = _calendar.ServerNowMs();
            int day = rules.DayNumberAt(nowMs);
            if (day != _day)
            {
                _day = day;
                _pending = true;
            }

            if (!_pending)
            {
                return;
            }
        }

        if (!_farm.IsLoaded)
        {
            return;
        }

        lock (_gate)
        {
            if (!_pending)
            {
                return;
            }

            _pending = false;
        }

        int atRisk = _farm.CountAtRisk(rules, nowMs, _placeables.SprinklerWateredSinceMs);
        if (atRisk > 0)
        {
            string text = atRisk == 1 ? _text["Farm_Thirsty_Morning_One"].Value : string.Format(CultureInfo.CurrentCulture, _text["Farm_Thirsty_Morning"].Value, atRisk);
            _messages.Notify(text);
        }
    }

    // The calendar the server sent, rebuilt only when its numbers change (once, at sign-in). Call under the gate.
    private GameCalendar Rules()
    {
        if (_rules is null || _rules.EpochMs != _calendar.EpochMs || _rules.DayLengthMs != _calendar.DayLengthMs)
        {
            _rules = new GameCalendar(_calendar.EpochMs, _calendar.DayLengthMs);
        }

        return _rules;
    }
}
