// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Nosum.Wasm.ViewModels.Device;

namespace Nosum.Wasm.Device;

/// <summary>The calls into <c>wwwroot/page-guard.js</c> (<c>window.nosumPage</c>): the device signals, the width watcher and the close-tab prompt. Used at start-up and on a session change, never from the frame.</summary>
[SupportedOSPlatform("browser")]
internal static partial class BrowserDevice
{
    /// <summary>Reads what the browser says about the device.</summary>
    /// <returns>The signals.</returns>
    public static DeviceSignals ReadSignals()
    {
        int hint = MobileHint();
        return new DeviceSignals(hint < 0 ? null : hint == 1, UserAgent(), Platform(), PointerCoarse(), CanHover(), MaxTouchPoints(), ViewportWidth());
    }

    /// <summary>Reads the <c>Device</c> section of <c>appsettings.json</c>; a missing key keeps the shipped value.</summary>
    /// <param name="configuration">The boot configuration.</param>
    /// <returns>The policy.</returns>
    public static DevicePolicy ReadPolicy(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        DevicePolicy shipped = DevicePolicy.Default;
        bool block = bool.TryParse(configuration["Device:BlockTouchDevices"], out bool parsedBlock) && parsedBlock;
        int minimum = int.TryParse(configuration["Device:MinimumWidth"], out int parsedMinimum) && parsedMinimum > 0 ? parsedMinimum : shipped.MinimumWidth;
        return new DevicePolicy(block, minimum);
    }

    [JSImport("globalThis.nosumPage.mobileHint")]
    private static partial int MobileHint();

    [JSImport("globalThis.nosumPage.userAgent")]
    private static partial string UserAgent();

    [JSImport("globalThis.nosumPage.platform")]
    private static partial string Platform();

    [JSImport("globalThis.nosumPage.pointerCoarse")]
    private static partial bool PointerCoarse();

    [JSImport("globalThis.nosumPage.canHover")]
    private static partial bool CanHover();

    [JSImport("globalThis.nosumPage.maxTouchPoints")]
    private static partial int MaxTouchPoints();

    /// <summary>Reads the window's inner width.</summary>
    /// <returns>CSS pixels.</returns>
    [JSImport("globalThis.nosumPage.viewportWidth")]
    public static partial int ViewportWidth();

    /// <summary>Calls back (with the window now at least <paramref name="minWidth"/> wide or not) each time the window crosses that width.</summary>
    /// <param name="minWidth">The minimum width in CSS pixels.</param>
    /// <param name="onChange">The callback.</param>
    [JSImport("globalThis.nosumPage.watchWidth")]
    public static partial void WatchWidth(int minWidth, [JSMarshalAs<JSType.Function<JSType.Boolean>>] Action<bool> onChange);

    /// <summary>Reads whether the HUD's menu column is collapsed into its Menu button (a phone or a touch screen).</summary>
    /// <returns><see langword="true"/> when collapsed.</returns>
    [JSImport("globalThis.nosumPage.menuColumnCollapsed")]
    public static partial bool MenuColumnCollapsed();

    /// <summary>Calls back each time the menu column collapses or opens again.</summary>
    /// <param name="onChange">The callback, with <see langword="true"/> when collapsed.</param>
    [JSImport("globalThis.nosumPage.watchMenuColumn")]
    public static partial void WatchMenuColumn([JSMarshalAs<JSType.Function<JSType.Boolean>>] Action<bool> onChange);

    /// <summary>Vibrates for a <see cref="ViewModels.Device.HapticKind"/> where the browser supports it; a no-op elsewhere.</summary>
    /// <param name="kind">The kind's number.</param>
    [JSImport("globalThis.nosumPage.vibrate")]
    public static partial void Vibrate(int kind);

    /// <summary>Turns the "really leave this page?" prompt on or off.</summary>
    /// <param name="on"><see langword="true"/> while the player is in the game.</param>
    [JSImport("globalThis.nosumPage.setUnloadGuard")]
    public static partial void SetUnloadGuard(bool on);
}
