// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Nodes;

namespace Nosum.Gameplay.Items;

/// <summary>
/// The pacing of every farm, node and tool action (docs/adr/ADR-074). The server refuses a use that comes sooner than
/// <see cref="MinIntervalMs"/> after the player's previous one; the client plays a swing of <see cref="SwingMs(ToolKind)"/> per action and
/// never sends sooner, so a mashed key or a fast drag repeats at the animation rate and is never refused.
/// </summary>
public static class ToolPacing
{
    /// <summary>The server's minimum time between two uses of one player, milliseconds (the swing cooldown, docs/adr/ADR-021 D8).</summary>
    public const int MinIntervalMs = NodeLimits.SwingCooldownMs;

    /// <summary>Rest after a swing ends before the next action may start, milliseconds.</summary>
    public const int RecoveryMs = 80;

    /// <summary>Where the hit frame falls in a swing, as a fraction of its length: the wind-up runs before it, the strike after.</summary>
    public const double HitFraction = 0.45;

    /// <summary>Energy, as a percentage of the maximum, at or below which the farmer is exhausted.</summary>
    public const int ExhaustedEnergyPercent = 25;

    /// <summary>Slowdown of an exhausted swing, as a percentage of the normal swing.</summary>
    public const int ExhaustedSlowdownPercent = 135;

    /// <summary>Gets the length of one swing of a tool, milliseconds. Nothing in hand (seeds, harvest, bare hands) swings at the reach pace.</summary>
    /// <param name="tool">The tool in hand, or <see cref="ToolKind.None"/>.</param>
    /// <returns>The swing length; never shorter than <see cref="MinIntervalMs"/>.</returns>
    public static int SwingMs(ToolKind tool) => tool switch
    {
        ToolKind.Hoe => 420,
        ToolKind.WateringCan => 460,
        ToolKind.Axe => 440,
        ToolKind.Pickaxe => 420,
        ToolKind.Scythe => 400,
        ToolKind.Sword => 360,
        ToolKind.Staff => 400,
        _ => 380,
    };

    /// <summary>Gets the length of a swing for a farmer, slower when exhausted.</summary>
    /// <param name="tool">The tool in hand, or <see cref="ToolKind.None"/>.</param>
    /// <param name="exhausted">Whether the farmer is exhausted (<see cref="IsExhausted"/>).</param>
    /// <returns>The swing length, milliseconds.</returns>
    public static int SwingMs(ToolKind tool, bool exhausted)
        => exhausted ? SwingMs(tool) * ExhaustedSlowdownPercent / 100 : SwingMs(tool);

    /// <summary>Gets whether a use at <paramref name="nowMs"/> comes too soon after the previous one: the server's rule.</summary>
    /// <param name="lastMs">When the previous use started, milliseconds; <see cref="long.MinValue"/> when there was none.</param>
    /// <param name="nowMs">When this use arrives, milliseconds.</param>
    /// <returns><see langword="true"/> when the use must be refused.</returns>
    public static bool IsTooSoon(long lastMs, long nowMs) => lastMs != long.MinValue && nowMs - lastMs < MinIntervalMs;

    /// <summary>Gets whether the farmer is exhausted: energy at or below <see cref="ExhaustedEnergyPercent"/> of the maximum.</summary>
    /// <param name="energy">Energy now.</param>
    /// <param name="maxEnergy">Maximum energy.</param>
    /// <returns><see langword="true"/> when swings are slower and a sweat bubble shows.</returns>
    public static bool IsExhausted(int energy, int maxEnergy) => maxEnergy > 0 && energy * 100 <= maxEnergy * ExhaustedEnergyPercent;
}
