// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.Extensions.Localization;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Device;

/// <summary>The device gate's screens: the verdict for this browser and the text of the notice that goes with it.</summary>
public sealed class DeviceGuardViewModel
{
    private readonly IStringLocalizer<Language> _text;
    private readonly DeviceSignals _signals;
    private readonly DevicePolicy _policy;
    private int _width;

    /// <summary>Initializes the view model.</summary>
    /// <param name="text">Localized UI text.</param>
    /// <param name="signals">What the browser reported at start-up.</param>
    /// <param name="policy">The switches.</param>
    public DeviceGuardViewModel(IStringLocalizer<Language> text, DeviceSignals signals, DevicePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(text);

        _text = text;
        _signals = signals;
        _policy = policy;
        _width = signals.ViewportWidth;
    }

    /// <summary>Raised when <see cref="Verdict"/> changes (the window crossed the minimum width).</summary>
    public event Action? Changed;

    /// <summary>Gets the current verdict.</summary>
    public DeviceVerdict Verdict => DeviceGate.Evaluate(_signals with { ViewportWidth = _width }, _policy);

    /// <summary>Gets the minimum window width the layout supports, in CSS pixels.</summary>
    public int MinimumWidth => _policy.MinimumWidth;

    /// <summary>Gets the localized text of the phone and tablet notice.</summary>
    public string BlockedText => _text["Device_BlockedText"].Value;

    /// <summary>Gets the localized title of the narrow window overlay.</summary>
    public string NarrowTitle => _text["Device_NarrowTitle"].Value;

    /// <summary>Gets the localized text of the narrow window overlay.</summary>
    public string NarrowText => _text["Device_NarrowText", _policy.MinimumWidth].Value;

    /// <summary>Records a new window width.</summary>
    /// <param name="width">The inner width in CSS pixels.</param>
    public void UpdateWidth(int width)
    {
        DeviceVerdict before = this.Verdict;
        _width = width;
        if (this.Verdict != before)
        {
            this.Changed?.Invoke();
        }
    }
}
