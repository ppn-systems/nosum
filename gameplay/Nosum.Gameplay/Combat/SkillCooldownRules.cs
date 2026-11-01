// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Combat;

/// <summary>
/// The cooldown of a combat skill (docs/adr/ADR-056 D5, amended 2026-10-31). A cast is admitted only when its skill is ready, and the server takes the
/// cooldown in the same step that admits the cast, under the same lock as its effect. A press that comes too early therefore changes nothing and is
/// answered with an explicit refusal. Time is a parameter (milliseconds on the server's monotonic clock), so the rule needs no clock in tests.
/// </summary>
public static class SkillCooldownRules
{
    /// <summary>Returns whether a skill is ready at a moment.</summary>
    /// <param name="readyAtMs">The moment the skill is ready again, milliseconds; zero when it was never cast.</param>
    /// <param name="nowMs">The moment of the cast, milliseconds.</param>
    /// <returns><see langword="true"/> at the ready instant and after it.</returns>
    public static bool IsReady(long readyAtMs, long nowMs) => nowMs >= readyAtMs;

    /// <summary>Returns the moment a skill is ready again once it was cast.</summary>
    /// <param name="nowMs">The moment of the cast, milliseconds.</param>
    /// <param name="skill">The skill cast.</param>
    /// <returns>The ready instant, milliseconds.</returns>
    public static long ReadyAfterCast(long nowMs, SkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        return nowMs + skill.CooldownMs;
    }

    /// <summary>Returns the time left before a skill is ready, never negative.</summary>
    /// <param name="readyAtMs">The ready instant, milliseconds.</param>
    /// <param name="nowMs">The current moment, milliseconds.</param>
    /// <returns>Milliseconds until ready; zero when ready.</returns>
    public static int RemainingMs(long readyAtMs, long nowMs) => (int)Math.Clamp(readyAtMs - nowMs, 0, int.MaxValue);
}
