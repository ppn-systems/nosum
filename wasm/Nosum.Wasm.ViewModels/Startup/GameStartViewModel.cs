// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using Microsoft.Extensions.Localization;
using Nosum.Wasm.ViewModels.Maps;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Startup;

/// <summary>The steps the game passes on its way from sign-in to the first frame, in order (<c>GameLoop.StartAsync</c>).</summary>
public enum GameStartStep
{
    /// <summary>Waiting for the server to say where the player stands.</summary>
    Connect = 0,

    /// <summary>Loading the map the player stands on.</summary>
    Map = 1,

    /// <summary>Loading the farmer, tool and item sprite sheets.</summary>
    Sprites = 2,

    /// <summary>Starting the renderer and the frame loop.</summary>
    Draw = 3,
}

/// <summary>
/// The overlay shown while "Entering the world" starts (issue #208): which step is running with a progress bar, gameplay tips that rotate, and after
/// <see cref="SlowAfter"/> a notice with a Try again action, so a stalled start is never a bare spinner. Raised from the start-up path and the timers,
/// never per frame.
/// </summary>
public sealed class GameStartViewModel : IObservableViewModel, IDisposable
{
    /// <summary>How long one tip stays on screen.</summary>
    public static readonly TimeSpan TipInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long the start may run before the Try again action appears.</summary>
    public static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(15);

    private static readonly string[] s_stepKeys = ["Game_Start_Step_Connect", "Game_Start_Step_World", "Game_Start_Step_Assets", "Game_Start_Step_World"];
    private static readonly int[] s_percent = [15, 45, 75, 95];

    private readonly IStringLocalizer<Language> _text;
    private readonly Timer _tipTimer;
    private readonly Timer _slowTimer;
    private GameStartStep _step;
    private int _tip;
    private bool _slow;
    private bool _visible;

    /// <summary>Initializes the view model; nothing is shown until <see cref="Begin"/>.</summary>
    /// <param name="text">Localized UI text.</param>
    public GameStartViewModel(IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(text);

        _text = text;
        _tipTimer = new Timer(_ => this.RotateTip(), null, Timeout.Infinite, Timeout.Infinite);
        _slowTimer = new Timer(_ => this.MarkSlow(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>Gets whether the overlay is shown.</summary>
    public bool IsVisible => _visible;

    /// <summary>Gets whether the start has run past <see cref="SlowAfter"/>; the Try again action shows then.</summary>
    public bool IsSlow => _slow;

    /// <summary>Gets how far the start has come, 0 to 100, from the step it is on.</summary>
    public int Percent => s_percent[(int)_step];

    /// <summary>Gets the name of the step that is running, localized.</summary>
    public string StepText => _text[s_stepKeys[(int)_step]];

    /// <summary>Gets the title, localized.</summary>
    public string Title => _text["Game_Loading"];

    /// <summary>Gets the tip on screen, localized.</summary>
    public string Tip => _text[MapLoadingViewModel.TipKeys[_tip]];

    /// <summary>Gets the reason shown once the start is slow, localized.</summary>
    public string SlowText => _text["Game_Start_Slow"];

    /// <summary>Gets the text of the Try again action, localized.</summary>
    public string RetryText => _text["Game_Start_Retry"];

    /// <summary>Shows the overlay at the first step with the first tip; the timers for tips and for "slow" start.</summary>
    public void Begin()
    {
        if (_visible)
        {
            return;
        }

        _visible = true;
        _slow = false;
        _step = GameStartStep.Connect;
        _tip = 0;
        _tipTimer.Change(TipInterval, TipInterval);
        _slowTimer.Change(SlowAfter, Timeout.InfiniteTimeSpan);
        this.Changed?.Invoke();
    }

    /// <summary>Moves the progress bar to a step; ignored while the overlay is hidden.</summary>
    /// <param name="step">The step that is running.</param>
    public void SetStep(GameStartStep step)
    {
        if (!_visible || step == _step)
        {
            return;
        }

        _step = step;
        this.Changed?.Invoke();
    }

    /// <summary>Hides the overlay and stops the timers.</summary>
    public void End()
    {
        if (!_visible)
        {
            return;
        }

        _visible = false;
        _slow = false;
        _tipTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _slowTimer.Change(Timeout.Infinite, Timeout.Infinite);
        this.Changed?.Invoke();
    }

    /// <summary>Shows the Try again action; called by the slow timer.</summary>
    public void MarkSlow()
    {
        if (!_visible || _slow)
        {
            return;
        }

        _slow = true;
        this.Changed?.Invoke();
    }

    /// <summary>Moves to the next tip; called by the tip timer.</summary>
    public void RotateTip()
    {
        _tip = (_tip + 1) % MapLoadingViewModel.TipKeys.Count;
        if (_visible)
        {
            this.Changed?.Invoke();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _tipTimer.Dispose();
        _slowTimer.Dispose();
    }
}
