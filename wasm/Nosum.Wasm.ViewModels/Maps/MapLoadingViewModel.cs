// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Localization;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Maps;

/// <summary>
/// The overlay shown while the screen is black and a map the server sent the player to is still loading: a progress bar and
/// gameplay tips that rotate while it waits. Raised from the frame and the load, never per frame; the tip timer is the only clock.
/// </summary>
public sealed class MapLoadingViewModel : IObservableViewModel, IDisposable
{
    /// <summary>How long one tip stays on screen.</summary>
    public static readonly TimeSpan TipInterval = TimeSpan.FromSeconds(5);

    private static readonly string[] s_tipKeys =
    [
        "Map_Tip_Merchant",
        "Map_Tip_Elevator",
        "Map_Tip_Sell",
        "Map_Tip_Use",
        "Map_Tip_Energy",
        "Map_Tip_Town",
    ];

    private readonly IStringLocalizer<Language> _text;
    private readonly Timer _tipTimer;
    private int _tip;
    private int _percent;
    private bool _visible;

    /// <summary>Gets the keys of the gameplay tips, in the order they rotate. Shared with the game's start-up overlay.</summary>
    public static IReadOnlyList<string> TipKeys { get; } = s_tipKeys;

    /// <summary>Initializes the view model; nothing is shown until <see cref="Begin"/>.</summary>
    /// <param name="text">Localized UI text.</param>
    public MapLoadingViewModel(IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(text);

        _text = text;
        _tipTimer = new Timer(_ => this.NextTip(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>Gets whether the overlay is shown.</summary>
    public bool IsVisible => _visible;

    /// <summary>Gets how far the map has loaded, 0 to 100.</summary>
    public int Percent => _percent;

    /// <summary>Gets the line above the bar, already localized.</summary>
    public string Title => _text["Map_Loading"];

    /// <summary>Gets the tip on screen, already localized.</summary>
    public string Tip => _text[s_tipKeys[_tip]];

    /// <summary>Shows the overlay with the first tip and starts rotating the tips.</summary>
    public void Begin()
    {
        if (_visible)
        {
            return;
        }

        _visible = true;
        _percent = 0;
        _tipTimer.Change(TipInterval, TipInterval);
        this.Changed?.Invoke();
    }

    /// <summary>Sets the progress bar, 0 to 100; ignored while the overlay is hidden.</summary>
    /// <param name="percent">How far the map has loaded.</param>
    public void SetPercent(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        if (!_visible || clamped == _percent)
        {
            return;
        }

        _percent = clamped;
        this.Changed?.Invoke();
    }

    /// <summary>Hides the overlay and stops rotating the tips.</summary>
    public void End()
    {
        if (!_visible)
        {
            return;
        }

        _visible = false;
        _tipTimer.Change(Timeout.Infinite, Timeout.Infinite);
        this.Changed?.Invoke();
    }

    /// <inheritdoc/>
    public void Dispose() => _tipTimer.Dispose();

    private void NextTip()
    {
        _tip = (_tip + 1) % s_tipKeys.Length;
        if (_visible)
        {
            this.Changed?.Invoke();
        }
    }
}
