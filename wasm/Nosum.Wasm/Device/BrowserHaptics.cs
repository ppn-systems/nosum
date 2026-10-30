// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.Versioning;
using Nosum.Wasm.ViewModels.Device;
using Nosum.Wasm.ViewModels.Settings;

namespace Nosum.Wasm.Device;

/// <summary>
/// Vibrates through <c>navigator.vibrate</c> (<c>wwwroot/page-guard.js</c>) when the player's haptics setting is on. One interop call per completed action;
/// nothing here runs per frame, and browsers without vibration (desktops, iOS Safari) ignore the call.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserHaptics : IHaptics
{
    private readonly SettingsViewModel _settings;

    /// <summary>Initializes the haptics.</summary>
    /// <param name="settings">The player's settings, read on each pulse so a change applies at once.</param>
    public BrowserHaptics(SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <inheritdoc/>
    public void Pulse(HapticKind kind)
    {
        if (_settings.Current.Haptics)
        {
            BrowserDevice.Vibrate((int)kind);
        }
    }
}
