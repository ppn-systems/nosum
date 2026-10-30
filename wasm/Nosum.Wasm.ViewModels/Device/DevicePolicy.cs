// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Device;

/// <summary>
/// The switches of the device gate. They are read from <c>wwwroot/appsettings.json</c> (section <c>Device</c>); the owner lifts the phone
/// block a build again by setting <c>Device:BlockTouchDevices</c> to <c>true</c>, with no code change. Phones and tablets play by default.
/// </summary>
/// <param name="BlockTouchDevices">Whether phones and tablets get the "please use a computer" screen instead of the game (and no connection to the server). Off by default (issue #216).</param>
/// <param name="MinimumWidth">The narrowest window, in CSS pixels, the layout supports; a narrower desktop window gets the "please widen the window" overlay.</param>
public readonly record struct DevicePolicy(bool BlockTouchDevices, int MinimumWidth)
{
    /// <summary>The width below which the in-game layout breaks (the menu window and the HUD regions need about 1024 CSS pixels).</summary>
    public const int DefaultMinimumWidth = 1024;

    /// <summary>Gets the shipped policy: phones and tablets play (with the touch controls), 1024 px minimum.</summary>
    public static DevicePolicy Default => new(false, DefaultMinimumWidth);
}
