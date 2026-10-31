// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Calendar;

namespace Nosum.Gameplay.Combat;

/// <summary>
/// Pure rules for status effects. A player holds two independent effect slots: the effect slot (regeneration or
/// poison, ticked every AI tick) and the buff slot (speed buffs, timed by the clock). A buff never replaces an effect
/// and an effect never replaces a buff (docs/adr/ADR-051, issue #146).
/// </summary>
public static class StatusEffectRules
{
    /// <summary>Duration of the <see cref="StatusEffectKind.BrotherhoodInArms"/> buff granted for a milestone boss kill (docs/adr/ADR-052).</summary>
    public const int BrotherhoodInArmsSeconds = 600;

    /// <summary>Duration of the speed buff a sanctuary shrine grants (issue #146); the shrine's old three-tick Swiftness lasted about this long.</summary>
    public const int ShrineBuffSeconds = 5;

    /// <summary>
    /// Duration of the speed tonic (docs/adr/ADR-081 D4): three game hours, in real seconds at the default day length
    /// (a game hour is a twenty-fourth of a game day, so 30 real minutes).
    /// </summary>
    public const int SpeedTonicSeconds = 3 * (int)(GameCalendar.DefaultDayLengthMs / 24 / 1000);

    /// <summary>Movement speed multiplier granted by <see cref="StatusEffectKind.Swiftness"/>.</summary>
    public const double SwiftnessSpeedFactor = 1.25;

    /// <summary>Movement speed multiplier granted by <see cref="StatusEffectKind.BrotherhoodInArms"/>.</summary>
    public const double BrotherhoodInArmsSpeedFactor = 1.05;

    /// <summary>Returns whether a status effect belongs in the buff slot.</summary>
    /// <param name="kind">Status effect kind.</param>
    /// <returns><see langword="true"/> for buffs; <see langword="false"/> for effects and <see cref="StatusEffectKind.None"/>.</returns>
    public static bool IsBuff(StatusEffectKind kind) => kind is StatusEffectKind.Swiftness or StatusEffectKind.BrotherhoodInArms;

    /// <summary>Returns the movement speed multiplier a buff grants; 1 when the buff is not a speed buff.</summary>
    /// <param name="buff">The buff held in the buff slot, or <see cref="StatusEffectKind.None"/>.</param>
    /// <returns>Multiplier applied to walking speed, at least 1.</returns>
    public static double MoveSpeedFactor(StatusEffectKind buff) => buff switch
    {
        StatusEffectKind.Swiftness => SwiftnessSpeedFactor,
        StatusEffectKind.BrotherhoodInArms => BrotherhoodInArmsSpeedFactor,
        _ => 1,
    };
}
