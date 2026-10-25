// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using Microsoft.Extensions.Localization;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Settings;

/// <summary>
/// The first-run hints (docs/adr/ADR-033 D3): one short line at a time, never a panel that blocks. A hint appears when the step before it is done, stays
/// <see cref="HintSeconds"/> seconds (the control fades it out), and is replaced by the next one when the player does what it says. Which steps are done is stored
/// with the settings, so the hints are shown once; they can be turned off or restarted in the settings.
/// </summary>
public sealed class OnboardingViewModel : IObservableViewModel, IDisposable
{
    /// <summary>How long a hint stays before it fades.</summary>
    public const int HintSeconds = 12;

    private readonly SettingsViewModel _settings;
    private readonly IStringLocalizer<Language> _text;
    private readonly TimeProvider _time;
    private ITimer? _timer;
    private bool _started;
    private bool _visible;
    private int _seen;

    /// <summary>Initializes the view model.</summary>
    /// <param name="settings">The settings, which keep the finished steps.</param>
    /// <param name="text">UI text.</param>
    /// <param name="time">The clock the fade timer uses; <see langword="null"/> for the system clock.</param>
    public OnboardingViewModel(SettingsViewModel settings, IStringLocalizer<Language> text, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(text);
        _settings = settings;
        _text = text;
        _time = time ?? TimeProvider.System;
        settings.Applied += this.OnSettings;
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>Gets whether a hint is showing.</summary>
    public bool IsVisible => _started && _visible && this.Current is not null;

    /// <summary>Gets the step whose hint is showing, or <see langword="null"/> when all are done or the hints are off.</summary>
    public TutorialStep? Current
    {
        get
        {
            if (!_settings.Current.TutorialEnabled)
            {
                return null;
            }

            int done = _settings.Current.TutorialDone | _seen;
            foreach (TutorialStep step in Enum.GetValues<TutorialStep>())
            {
                if ((done & TutorialSteps.Bit(step)) == 0)
                {
                    return step;
                }
            }

            return null;
        }
    }

    /// <summary>Gets the localized text of the hint.</summary>
    public string Hint => this.Current is { } step
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, _text["Hint_" + step].Value, this.KeyFor(step))
        : string.Empty;

    /// <summary>Gets the localized accessible name of the hint.</summary>
    public string Label => _text["Hint_Label"].Value;

    /// <summary>Starts showing hints; call once the game is running.</summary>
    public void Start()
    {
        _started = true;
        this.Show();
    }

    /// <summary>Tells the hints the player did something.</summary>
    /// <param name="step">What was done.</param>
    public void Signal(TutorialStep step)
    {
        int bit = TutorialSteps.Bit(step);
        if (((_settings.Current.TutorialDone | _seen) & bit) != 0)
        {
            return;
        }

        // A step done before its hint came up counts too, but the player is not told about it afterwards.
        _seen |= bit;
        _settings.MarkTutorialStep(step);
    }

    /// <summary>Hides the hint that is showing (called when its time is up).</summary>
    public void Expire()
    {
        if (_visible)
        {
            _visible = false;
            this.Changed?.Invoke();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _settings.Applied -= this.OnSettings;
        _timer?.Dispose();
    }

    private string KeyFor(TutorialStep step) => step switch
    {
        TutorialStep.Move => string.Join(' ', _settings.CapOf(GameAction.MoveUp), _settings.CapOf(GameAction.MoveLeft), _settings.CapOf(GameAction.MoveDown), _settings.CapOf(GameAction.MoveRight)),
        TutorialStep.Use or TutorialStep.Sell => _settings.CapOf(GameAction.Use),
        _ => string.Empty,
    };

    // A settings change (hints turned on again, restarted) shows the first hint that is left.
    private void OnSettings(ClientSettings settings)
    {
        _seen &= settings.TutorialDone == 0 ? 0 : -1;
        if (_started)
        {
            this.Show();
        }
    }

    private void Show()
    {
        _timer?.Dispose();
        _timer = null;
        _visible = this.Current is not null;
        if (_visible)
        {
            _timer = _time.CreateTimer(_ => this.Expire(), null, TimeSpan.FromSeconds(HintSeconds), Timeout.InfiniteTimeSpan);
        }

        this.Changed?.Invoke();
    }
}
