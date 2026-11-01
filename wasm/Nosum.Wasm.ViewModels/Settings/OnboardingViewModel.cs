// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Quest;
using Nosum.Gameplay.Quests;
using Nosum.Wasm.Socket.Quest;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Settings;

/// <summary>
/// The first-run hints (docs/adr/ADR-033 D3): one short line at a time, never a panel that blocks. A hint appears when the step before it is done and the player
/// has been idle for <see cref="IdleSeconds"/> seconds (so it never lands on top of an action in progress), stays <see cref="HintSeconds"/> seconds (the control
/// fades it out), and is replaced by the next one when the player does what it says. Which steps are done is stored
/// with the settings, so the hints are shown once; they can be turned off or restarted in the settings.
/// Once the player has walked and used a tool, the hints are the steps of the tutorial quest chain the server keeps (docs/adr/ADR-036 D4): the line shown is the
/// current step's tip and its reward. The Sell and Shop hints of the first-run list are superseded by chain steps and never show.
/// </summary>
public sealed class OnboardingViewModel : IObservableViewModel, IDisposable
{
    /// <summary>How long a hint stays before it fades.</summary>
    public const int HintSeconds = 12;

    /// <summary>How long the player must give the game no input (keys or touch) before a new hint comes up (docs/adr/ADR-033 D3).</summary>
    public const int IdleSeconds = 2;

    private readonly SettingsViewModel _settings;
    private readonly IStringLocalizer<Language> _text;
    private readonly TimeProvider _time;
    private readonly IStringLocalizer<Catalogue>? _catalogue;
    private readonly QuestChannel? _quests;
    private string? _shownKey;
    private ITimer? _timer;
    private ITimer? _idle;
    private bool _pending;
    private long _activeAt;
    private bool _started;
    private bool _visible;
    private int _seen;

    /// <summary>Initializes the view model.</summary>
    /// <param name="settings">The settings, which keep the finished steps.</param>
    /// <param name="text">UI text.</param>
    /// <param name="time">The clock the fade timer uses; <see langword="null"/> for the system clock.</param>
    /// <param name="catalogue">Quest tips; <see langword="null"/> to show no quest hints.</param>
    /// <param name="quests">The quest mirror the chain step comes from; <see langword="null"/> to show no quest hints.</param>
    public OnboardingViewModel(SettingsViewModel settings, IStringLocalizer<Language> text, TimeProvider? time = null, IStringLocalizer<Catalogue>? catalogue = null, QuestChannel? quests = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(text);
        _settings = settings;
        _text = text;
        _time = time ?? TimeProvider.System;
        _catalogue = catalogue;
        _quests = quests;
        settings.Applied += this.OnSettings;
        if (quests is not null)
        {
            quests.Changed += this.OnQuests;
        }
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>Gets whether a hint is showing.</summary>
    public bool IsVisible => _started && _visible && this.HintKey is not null;

    /// <summary>Gets the first-run step whose hint is next, or <see langword="null"/> when all are done or the hints are off. The Sell and Shop steps are skipped (docs/adr/ADR-036 D4).</summary>
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
                if (step is TutorialStep.Sell or TutorialStep.Shop)
                {
                    continue;
                }

                if ((done & TutorialSteps.Bit(step)) == 0)
                {
                    return step;
                }
            }

            return null;
        }
    }

    /// <summary>Gets the quest of the tutorial chain whose tip is next: the first one in chain order that is not finished. <see langword="null"/> while the quests are not loaded, when the chain is done or the hints are off.</summary>
    public QuestDefinition? CurrentQuest
    {
        get
        {
            if (_quests is null || _catalogue is null || !_settings.Current.TutorialEnabled)
            {
                return null;
            }

            IReadOnlyList<QuestInfo> mirror = _quests.Quests();
            foreach (int questId in QuestCatalog.Chain)
            {
                foreach (QuestInfo quest in mirror)
                {
                    if (quest.QuestId == questId && quest.State == QuestState.Active && QuestCatalog.TryGet(questId, out QuestDefinition? definition))
                    {
                        return definition;
                    }
                }
            }

            return null;
        }
    }

    /// <summary>Gets what the showing hint is about, used to tell one hint from the next: the step name (<c>move</c>), <c>quest:&lt;id&gt;</c>, or <see langword="null"/> when there is none. Walking and using a tool come first, the chain next, the rest after it.</summary>
    public string? HintKey
    {
        get
        {
            TutorialStep? step = this.Current;
            if (step is TutorialStep.Move or TutorialStep.Use)
            {
                return step.ToString();
            }

            if (this.CurrentQuest is { } quest)
            {
                return "quest:" + quest.Id.ToString(CultureInfo.InvariantCulture);
            }

            return step?.ToString();
        }
    }

    /// <summary>Gets the localized text of the hint.</summary>
    public string Hint
    {
        get
        {
            TutorialStep? step = this.Current;
            if (step is not (TutorialStep.Move or TutorialStep.Use) && this.CurrentQuest is { } quest)
            {
                string tip = _catalogue!["Quest_" + quest.Key + "_Tip"].Value;
                return quest.RewardCoins > 0
                    ? string.Format(CultureInfo.CurrentCulture, _text["Hint_Quest"].Value, tip, quest.RewardCoins.ToString("N0", CultureInfo.CurrentCulture))
                    : tip;
            }

            return step is { } first
                ? string.Format(CultureInfo.CurrentCulture, _text["Hint_" + first].Value, this.KeyFor(first))
                : string.Empty;
        }
    }

    /// <summary>Gets the localized accessible name of the hint.</summary>
    public string Label => _text["Hint_Label"].Value;

    /// <summary>Starts showing hints; call once the game is running. The first hint comes up once the player has been idle for <see cref="IdleSeconds"/>.</summary>
    public void Start()
    {
        _started = true;
        this.Request();
    }

    /// <summary>Tells the hints the player is giving the game input. A hint waiting for the player to be idle waits a further <see cref="IdleSeconds"/>.</summary>
    public void NoteActivity()
    {
        if (_pending)
        {
            // Only the time is kept: the frame calls this every frame the player is giving input, so it must not start a timer (ADR-095 D3).
            _activeAt = _time.GetTimestamp();
        }
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
        if (_quests is not null)
        {
            _quests.Changed -= this.OnQuests;
        }

        _timer?.Dispose();
        _idle?.Dispose();
    }

    private string KeyFor(TutorialStep step) => step switch
    {
        TutorialStep.Move => string.Join(' ', _settings.CapOf(GameAction.MoveUp), _settings.CapOf(GameAction.MoveLeft), _settings.CapOf(GameAction.MoveDown), _settings.CapOf(GameAction.MoveRight)),
        TutorialStep.Use or TutorialStep.Sell => _settings.CapOf(GameAction.Use),
        _ => string.Empty,
    };

    // A settings change (hints turned on again, restarted) asks for the first hint that is left, unless that hint is already on its way or on screen.
    private void OnSettings(ClientSettings settings)
    {
        _seen &= settings.TutorialDone == 0 ? 0 : -1;
        if (_started && !((_visible || _pending) && string.Equals(this.HintKey, _shownKey, StringComparison.Ordinal)))
        {
            this.Request();
        }
    }

    // A new current step asks for its hint; progress inside the same step does not ask again.
    private void OnQuests()
    {
        if (_started && !string.Equals(this.HintKey, _shownKey, StringComparison.Ordinal))
        {
            this.Request();
        }
    }

    // Hides the hint on screen and waits for the player to be idle before showing the current one.
    private void Request()
    {
        _timer?.Dispose();
        _timer = null;
        _visible = false;
        _shownKey = this.HintKey;
        _pending = _shownKey is not null;
        if (_pending)
        {
            this.ArmIdle();
        }
        else
        {
            _idle?.Dispose();
            _idle = null;
        }

        this.Changed?.Invoke();
    }

    private void ArmIdle()
    {
        _activeAt = _time.GetTimestamp();
        _idle?.Dispose();
        _idle = _time.CreateTimer(_ => this.OnIdle(), null, TimeSpan.FromSeconds(IdleSeconds), Timeout.InfiniteTimeSpan);
    }

    // The timer fires IdleSeconds after the last wait started. If the player gave input since, it waits out the rest of the quiet time instead.
    private void OnIdle()
    {
        _idle?.Dispose();
        _idle = null;
        TimeSpan quiet = _time.GetElapsedTime(_activeAt);
        if (quiet < TimeSpan.FromSeconds(IdleSeconds))
        {
            _idle = _time.CreateTimer(_ => this.OnIdle(), null, TimeSpan.FromSeconds(IdleSeconds) - quiet, Timeout.InfiniteTimeSpan);
            return;
        }

        _pending = false;
        _visible = _shownKey is not null;
        if (_visible)
        {
            _timer = _time.CreateTimer(_ => this.Expire(), null, TimeSpan.FromSeconds(HintSeconds), Timeout.InfiniteTimeSpan);
        }

        this.Changed?.Invoke();
    }
}
