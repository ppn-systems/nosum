// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Text.RegularExpressions;

namespace Nosum.Wasm.ViewModels.Device;

/// <summary>What the page may show for a device.</summary>
public enum DeviceVerdict
{
    /// <summary>A computer with a wide enough window: the game.</summary>
    Allowed = 0,

    /// <summary>A phone or tablet while <see cref="DevicePolicy.BlockTouchDevices"/> is on: the notice, and no connection to the server.</summary>
    TouchBlocked = 1,

    /// <summary>A computer whose window is narrower than <see cref="DevicePolicy.MinimumWidth"/>: the game waits behind a "widen the window" overlay.</summary>
    TooNarrow = 2,
}

/// <summary>
/// Decides whether a device may play. A phone or tablet is recognised by any one of: the UA Client Hints <c>mobile</c> flag, a mobile word in the user agent
/// (the fallback for browsers without Client Hints), an iPad that reports a Mac user agent (a Mac with touch points), or a touch-first input
/// (<c>pointer: coarse</c>, no hover, touch points present). The viewport width is deliberately not a phone signal: a small desktop window must get the
/// "widen the window" overlay, not the phone notice.
/// </summary>
public static partial class DeviceGate
{
    /// <summary>Evaluates the signals under a policy.</summary>
    /// <param name="signals">What the browser reported.</param>
    /// <param name="policy">The switches.</param>
    /// <returns>The verdict.</returns>
    public static DeviceVerdict Evaluate(DeviceSignals signals, DevicePolicy policy)
    {
        if (policy.BlockTouchDevices && IsTouchDevice(signals))
        {
            return DeviceVerdict.TouchBlocked;
        }

        return signals.ViewportWidth < policy.MinimumWidth ? DeviceVerdict.TooNarrow : DeviceVerdict.Allowed;
    }

    /// <summary>Tells whether the signals describe a phone or a tablet, whatever the policy.</summary>
    /// <param name="signals">What the browser reported.</param>
    /// <returns><see langword="true"/> for a phone or tablet.</returns>
    public static bool IsTouchDevice(DeviceSignals signals)
    {
        if (signals.MobileHint == true)
        {
            return true;
        }

        if (MobileUserAgent().IsMatch(signals.UserAgent ?? string.Empty))
        {
            return true;
        }

        // iPadOS 13+ sends a Mac user agent; a real Mac has no touch screen.
        if (string.Equals(signals.Platform, "MacIntel", StringComparison.Ordinal) && signals.MaxTouchPoints > 1)
        {
            return true;
        }

        return signals.PointerCoarse && !signals.CanHover && signals.MaxTouchPoints > 0;
    }

    [GeneratedRegex("Android|iPhone|iPad|iPod|Mobile|Tablet|Silk/|Kindle|webOS|BlackBerry|Opera Mini|IEMobile", RegexOptions.CultureInvariant)]
    private static partial Regex MobileUserAgent();
}
