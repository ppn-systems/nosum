// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Device;

/// <summary>What a short vibration marks (docs/adr/ADR-033 D2).</summary>
public enum HapticKind
{
    /// <summary>A small tick: a click that did something.</summary>
    Light = 0,

    /// <summary>A purchase or sale went through.</summary>
    Success = 1,

    /// <summary>The server refused the action (no coins, no room, not in reach).</summary>
    Refusal = 2,
}

/// <summary>
/// Short vibration feedback for the player's actions. Called from view models on a completed action, never from the frame. An implementation checks the
/// player's setting and does nothing where the browser has no vibration.
/// </summary>
public interface IHaptics
{
    /// <summary>Vibrates for an event.</summary>
    /// <param name="kind">What happened.</param>
    void Pulse(HapticKind kind);
}

/// <summary>The implementation used where there is no vibration at all (tests, a desktop host).</summary>
public sealed class NoHaptics : IHaptics
{
    /// <summary>Gets the shared instance.</summary>
    public static NoHaptics Instance { get; } = new();

    /// <inheritdoc/>
    public void Pulse(HapticKind kind)
    {
        _ = kind;
    }
}
