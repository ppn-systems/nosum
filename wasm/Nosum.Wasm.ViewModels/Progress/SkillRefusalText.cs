// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Contracts.Combat;
using Nosum.Gameplay.Combat;

namespace Nosum.Wasm.ViewModels.Progress;

/// <summary>
/// The message of a combat skill cast the server refused (docs/adr/ADR-056 D5). Every refusal the server can send for a cast has a message, so a press
/// that the server does not accept is never silent. The message is a resource key (every visible string goes through .resx), resolved by the caller in the player's language.
/// </summary>
public static class SkillRefusalText
{
    /// <summary>Returns the resource key that says why a skill cast was refused.</summary>
    /// <param name="status">The status the server replied with.</param>
    /// <returns>The resource key, or <see langword="null"/> when the cast landed or the status is not one a skill cast can be refused with.</returns>
    public static string? KeyFor(CombatStatus status) => status switch
    {
        CombatStatus.NotJoined => "Combat_Refused_NotJoined",
        CombatStatus.UnknownSkill => "Combat_Refused_UnknownSkill",
        CombatStatus.UnknownMonster => "Combat_Refused_UnknownMonster",
        CombatStatus.OutOfRange => "Combat_Refused_OutOfRange",
        CombatStatus.NotEnoughMp => "Combat_NotEnoughMp",
        CombatStatus.AlreadyFull => "Combat_Refused_AlreadyFull",
        CombatStatus.OnCooldown => "Combat_Refused_OnCooldown",
        _ => null,
    };
}

/// <summary>
/// Why a skill press is refused on the client before anything is sent (docs/adr/ADR-056 D5). The same rules as the server's, checked first so a
/// press that cannot land still says why: a cooldown, too little MP, full health for the heal, or no monster in reach for Whirlwind (Z) and
/// Fireball (X). The server still decides every outcome; this only names a refusal the player would otherwise not see.
/// </summary>
public static class SkillPressRefusal
{
    /// <summary>Returns the resource key that says why a press is refused, or <see langword="null"/> when the press may be sent.</summary>
    /// <param name="kind">The skill.</param>
    /// <param name="onCooldown">Whether the skill is still recharging.</param>
    /// <param name="mp">The player's MP.</param>
    /// <param name="mpCost">The skill's MP cost.</param>
    /// <param name="targetInReach">Whether a monster is in the skill's range (only Whirlwind and Fireball need one).</param>
    /// <param name="hpFull">Whether the player's health is already full (only the heal cares).</param>
    /// <returns>The resource key of the refusal, or <see langword="null"/> when the cast may be sent.</returns>
    public static string? KeyFor(CombatSkillKind kind, bool onCooldown, int mp, int mpCost, bool targetInReach, bool hpFull)
    {
        if (onCooldown)
        {
            return "Combat_Refused_OnCooldown";
        }

        if (mp < mpCost)
        {
            return "Combat_NotEnoughMp";
        }

        return kind switch
        {
            CombatSkillKind.DivineHeal when hpFull => "Combat_Refused_AlreadyFull",
            CombatSkillKind.Whirlwind or CombatSkillKind.Fireball when !targetInReach => "Combat_Refused_NoTarget",
            _ => null,
        };
    }
}
