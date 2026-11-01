// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Wasm.ViewModels.Localization;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Settings;

/// <summary>
/// View model of the client settings (docs/adr/ADR-033): holds the <see cref="ClientSettings"/>, stores every change through an <see cref="ISettingsStore"/> and tells
/// whoever applies them (<see cref="Applied"/>). The account page (sign out, language) stays with the account view model.
/// </summary>
public sealed class SettingsViewModel : PanelViewModel, IDisposable
{
    /// <summary>The zoom choices: 0 is automatic.</summary>
    public static IReadOnlyList<int> ScaleChoices { get; } = [0, 2, 3, 4, 5, 6];

    private readonly ISettingsStore _store;
    private ClientSettings _current;
    private GameAction? _listening;
    private bool _systemReducesMotion;
    private bool _narrowScreen;

    /// <summary>Initializes the view model and reads the stored settings.</summary>
    /// <param name="text">UI text.</param>
    /// <param name="store">Where the settings are kept.</param>
    public SettingsViewModel(IStringLocalizer<Language> text, ISettingsStore store)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _current = ClientSettings.Parse(store.Read());
        UiCulture.Changed += this.OnCultureChanged;
    }

    /// <summary>Raised after the settings changed (and by <see cref="Apply"/>), with the new value. May run on any thread.</summary>
    public event Action<ClientSettings>? Applied;

    /// <summary>Gets the settings.</summary>
    public ClientSettings Current => _current;

    /// <summary>Gets whether motion is reduced now: the player's choice, or the system's preference while the player has not chosen.</summary>
    public bool ReducesMotion => _current.ReducesMotion(_systemReducesMotion);

    /// <summary>Gets whether the minimap is hidden now: the player's choice, or on a narrow screen while the player has not chosen.</summary>
    public bool MinimapHiddenNow => _current.MinimapHiddenNow(_narrowScreen);

    /// <summary>Gets the action whose key is being chosen (the next key pressed is bound to it), or <see langword="null"/>.</summary>
    public GameAction? Listening => _listening;

    /// <inheritdoc/>
    public void Dispose() => UiCulture.Changed -= this.OnCultureChanged;

    /// <summary>Raises <see cref="Applied"/> with the current settings, so the page picks them up at start.</summary>
    public void Apply() => this.Applied?.Invoke(_current);

    /// <summary>Tells the view model whether the system asks for reduced motion; it changes what <see cref="ReducesMotion"/> gives while the player has not chosen.</summary>
    /// <param name="prefers">The system's <c>prefers-reduced-motion</c> setting.</param>
    public void SetSystemReducesMotion(bool prefers)
    {
        if (_systemReducesMotion == prefers)
        {
            return;
        }

        _systemReducesMotion = prefers;
        this.Applied?.Invoke(_current);
        this.Raise();
    }

    /// <summary>Tells the view model whether the screen is narrow (a phone); the minimap is hidden there until the player chooses.</summary>
    /// <param name="narrow">Whether the screen is narrow.</param>
    public void SetNarrowScreen(bool narrow)
    {
        if (_narrowScreen == narrow)
        {
            return;
        }

        _narrowScreen = narrow;
        this.Applied?.Invoke(_current);
        this.Raise();
    }

    /// <summary>Shows or hides the minimap card (Settings &gt; Display); the player's choice is kept per browser.</summary>
    /// <param name="hidden">Whether the minimap is hidden.</param>
    public void SetMinimapHidden(bool hidden) => this.Update(c => c with { MinimapHidden = hidden });

    /// <summary>Sets the music volume.</summary>
    /// <param name="percent">0 to 100.</param>
    public void SetMusicVolume(int percent) => this.Update(c => c with { MusicVolume = Math.Clamp(percent, 0, 100) });

    /// <summary>Sets the sound effects volume.</summary>
    /// <param name="percent">0 to 100.</param>
    public void SetSoundVolume(int percent) => this.Update(c => c with { SoundVolume = Math.Clamp(percent, 0, 100) });

    /// <summary>Mutes or unmutes everything.</summary>
    /// <param name="muted">Whether to mute.</param>
    public void SetMuted(bool muted) => this.Update(c => c with { Muted = muted });

    /// <summary>Sets the zoom.</summary>
    /// <param name="scale">0 for automatic, otherwise 2 to 6.</param>
    public void SetUiScale(int scale) => this.Update(c => c with { UiScale = scale is >= 2 and <= 6 ? scale : 0 });

    /// <summary>Shows or hides the names over the farmers' heads.</summary>
    /// <param name="show">Whether to show them.</param>
    public void SetShowNames(bool show) => this.Update(c => c with { ShowNames = show });

    /// <summary>Turns the on-screen touch controls on or off.</summary>
    /// <param name="on">Whether to show them on touch devices.</param>
    public void SetTouchControls(bool on) => this.Update(c => c with { TouchControls = on });

    /// <summary>Turns vibration feedback on or off.</summary>
    /// <param name="on">Whether to vibrate on purchases, sales and refusals.</param>
    public void SetHaptics(bool on) => this.Update(c => c with { Haptics = on });

    /// <summary>Turns reduced motion on or off, overriding the system's preference.</summary>
    /// <param name="reduce">Whether motion is reduced.</param>
    public void SetReduceMotion(bool reduce) => this.Update(c => c with { ReduceMotion = reduce });

    /// <summary>Lets the system's reduced-motion preference decide again.</summary>
    public void FollowSystemMotion() => this.Update(c => c with { ReduceMotion = null });

    /// <summary>Sets the panel and HUD scale.</summary>
    /// <param name="percent"><see cref="ClientSettings.MinPanelScale"/> to <see cref="ClientSettings.MaxPanelScale"/>; other values are clamped.</param>
    public void SetPanelScale(int percent) => this.Update(c => c with { PanelScale = Math.Clamp(percent, ClientSettings.MinPanelScale, ClientSettings.MaxPanelScale) });

    /// <summary>Turns the high contrast theme on or off.</summary>
    /// <param name="high">Whether it is on.</param>
    public void SetHighContrast(bool high) => this.Update(c => c with { HighContrast = high });

    /// <summary>Turns the first-run hints on or off.</summary>
    /// <param name="enabled">Whether they are on.</param>
    public void SetTutorialEnabled(bool enabled) => this.Update(c => c with { TutorialEnabled = enabled });

    /// <summary>Shows the first-run hints again from the first step.</summary>
    public void RestartTutorial() => this.Update(c => c with { TutorialEnabled = true, TutorialDone = 0 });

    /// <summary>Records a finished hint step.</summary>
    /// <param name="step">The step.</param>
    public void MarkTutorialStep(TutorialStep step) => this.Update(c => c with { TutorialDone = c.TutorialDone | TutorialSteps.Bit(step) });

    /// <summary>Gets the key cap printed for an action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>For example <c>E</c>.</returns>
    public string CapOf(GameAction action) => KeyBindings.CapOf(_current.Bindings.CodeOf(action));

    /// <summary>Gets the localized name of an action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>For example "Inventory".</returns>
    public string LabelOf(GameAction action) => this.Text("Action_" + action);

    /// <summary>Starts waiting for the key to bind to an action.</summary>
    /// <param name="action">The action.</param>
    public void BeginRebind(GameAction action)
    {
        _listening = action;
        this.ClearStatus();
        this.Raise();
    }

    /// <summary>Stops waiting for a key.</summary>
    public void CancelRebind()
    {
        if (_listening is not null)
        {
            _listening = null;
            this.Raise();
        }
    }

    /// <summary>Binds the key pressed to the action being chosen.</summary>
    /// <param name="code">The browser key code.</param>
    /// <returns>What happened; <see cref="RebindResult.Reserved"/> keeps waiting for another key.</returns>
    public RebindResult Capture(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (_listening is not { } action)
        {
            return RebindResult.Reserved;
        }

        GameAction? other = null;
        foreach (GameAction candidate in Enum.GetValues<GameAction>())
        {
            if (candidate != action && string.Equals(_current.Bindings.CodeOf(candidate), code, StringComparison.Ordinal))
            {
                other = candidate;
            }
        }

        RebindResult result = _current.Bindings.TryRebind(action, code, out KeyBindings next);
        if (result == RebindResult.Reserved)
        {
            this.SetStatus(this.Text("Settings_KeyReserved", KeyBindings.CapOf(code)));
            this.Raise();
            return result;
        }

        _listening = null;
        this.Update(c => c with { Bindings = next });
        this.SetStatus(result == RebindResult.Swapped && other is { } swapped ? this.Text("Settings_KeySwapped", this.LabelOf(swapped), this.CapOf(swapped)) : string.Empty, good: true);
        this.Raise();
        return result;
    }

    /// <summary>Puts every key back to its default.</summary>
    public void ResetBindings()
    {
        _listening = null;
        this.Update(c => c with { Bindings = KeyBindings.Default });
        this.ClearStatus();
    }

    private void Update(Func<ClientSettings, ClientSettings> change)
    {
        ClientSettings next = change(_current);
        if (next == _current)
        {
            return;
        }

        _current = next;
        _store.Write(next.Serialize());
        this.Applied?.Invoke(next);
        this.Raise();
    }

    // The language is chosen on the account page and applied from the account at sign-in; either way the browser remembers the last one used.
    private void OnCultureChanged()
    {
        Nosum.Contracts.Common.GameLanguage language = UiCulture.Current;
        this.Update(c => c with { Language = language });
    }
}
