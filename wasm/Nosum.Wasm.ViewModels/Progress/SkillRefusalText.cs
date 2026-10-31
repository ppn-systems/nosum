// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Contracts.Combat;

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
