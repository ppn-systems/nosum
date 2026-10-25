// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Runtime.Versioning;
using Nosum.Rendering;
using Nosum.Rendering.Web;
using Nosum.Wasm.Game;
using Nosum.Wasm.ViewModels.Settings;

namespace Nosum.Wasm.Settings;

/// <summary>Puts the settings to work: the page switches and the zoom in the browser, the key bindings in the input handler, the drawing options in the game.</summary>
[SupportedOSPlatform("browser")]
internal sealed class SettingsApplier
{
    private int _scale = -1;
    private bool? _contrast;
    private bool? _motion;
    private string? _keys;
    private (int Music, int Sound, bool Muted)? _volumes;

    /// <summary>Applies settings; only what changed since the last call reaches the browser.</summary>
    /// <param name="settings">The settings.</param>
    public void Apply(ClientSettings settings)
    {
        ViewOptions.Set(settings.UiScale, settings.ShowNames, settings.ReduceMotion);

        if (_contrast != settings.HighContrast || _motion != settings.ReduceMotion)
        {
            _contrast = settings.HighContrast;
            _motion = settings.ReduceMotion;
            BrowserSettings.ApplyDisplay(settings.HighContrast, settings.ReduceMotion);
        }

        if (_scale != settings.UiScale)
        {
            _scale = settings.UiScale;
            BrowserSettings.ApplyScale(settings.UiScale);
        }

        (int, int, bool) volumes = (settings.MusicVolume, settings.SoundVolume, settings.Muted);
        if (_volumes != volumes)
        {
            _volumes = volumes;
            WebRenderBackend.SetLevels(VolumeCurve.Gain(settings.MusicVolume, settings.Muted), VolumeCurve.Gain(settings.SoundVolume, settings.Muted));
        }

        string keys = settings.Bindings.SerializeAll();
        if (!string.Equals(_keys, keys, System.StringComparison.Ordinal))
        {
            _keys = keys;
            WebRenderBackend.SetBindings(keys);
        }
    }
}
