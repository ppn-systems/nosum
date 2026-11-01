// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Contracts.Combat;

namespace Nosum.Wasm.Socket.Combat;

/// <summary>Client-side snapshot of one monster on the active map.</summary>
/// <param name="MonsterId">Monster id within the map/floor.</param>
/// <param name="Kind">Monster species byte (<c>MonsterKind</c>).</param>
/// <param name="X">Tile X.</param>
/// <param name="Y">Tile Y.</param>
/// <param name="Hp">Current HP.</param>
/// <param name="MaxHp">Maximum HP.</param>
/// <param name="Modifier">Elite affix modifier (docs/adr/ADR-058).</param>
/// <param name="Enraged">Whether a boss is enraged at or below its HP threshold (docs/adr/ADR-047).</param>
public readonly record struct MonsterClientRow(int MonsterId, byte Kind, int X, int Y, int Hp, int MaxHp, byte Modifier = 0, bool Enraged = false);

/// <summary>One boss ground slam telegraph: the area shown until its impact (docs/adr/ADR-047).</summary>
/// <param name="X">Centre tile X.</param>
/// <param name="Y">Centre tile Y.</param>
/// <param name="RadiusTiles">Struck radius in tiles.</param>
/// <param name="StartMs">Local tick time when the telegraph was received.</param>
/// <param name="DelayMs">Milliseconds from receipt until the impact.</param>
public readonly record struct GroundTelegraphClientRow(int X, int Y, int RadiusTiles, long StartMs, int DelayMs);

/// <summary>An ordinary monster's melee strike in its wind-up (docs/adr/ADR-094), shown until the strike lands.</summary>
/// <param name="MonsterId">Monster id within the map/floor.</param>
/// <param name="StartMs">Local tick time when the telegraph was received.</param>
/// <param name="DelayMs">Milliseconds from receipt until the strike lands.</param>
public readonly record struct StrikeWindupClientRow(int MonsterId, long StartMs, int DelayMs);

/// <summary>One environmental hazard on the current map or dungeon floor (docs/adr/ADR-061).</summary>
/// <param name="HazardId">Hazard id.</param>
/// <param name="Kind">Hazard kind (1=SporeVent, 2=SpikeTrap, 3=MagmaFissure).</param>
/// <param name="X">Tile X.</param>
/// <param name="Y">Tile Y.</param>
/// <param name="Damage">Base damage per tick.</param>
public readonly record struct HazardClientRow(byte HazardId, byte Kind, ushort X, ushort Y, byte Damage);

/// <summary>Outcome of a client attack intent.</summary>
/// <param name="Status">Combat status.</param>
/// <param name="MonsterId">Target monster id.</param>
/// <param name="DamageDealt">Damage dealt to the monster.</param>
/// <param name="MonsterHp">Remaining monster HP.</param>
/// <param name="DamageTaken">Counter-attack damage taken by the player.</param>
/// <param name="KnockedOut">Whether the player was knocked out and revived.</param>
public readonly record struct AttackInfo(
    CombatStatus Status,
    int MonsterId,
    int DamageDealt,
    int MonsterHp,
    int DamageTaken,
    bool KnockedOut);

/// <summary>Summary of a cast skill result (docs/adr/ADR-056).</summary>
public readonly record struct SkillInfo(
    CombatStatus Status,
    byte SkillId,
    ushort DamageDealt,
    byte TargetsHit,
    ushort HealAmount,
    ushort CooldownMs);

/// <summary>Active in-flight spell or ranged projectile (docs/adr/ADR-048).</summary>
/// <param name="StartX">Origin tile X.</param>
/// <param name="StartY">Origin tile Y.</param>
/// <param name="TargetX">Destination tile X.</param>
/// <param name="TargetY">Destination tile Y.</param>
/// <param name="StartTime">Timestamp in milliseconds.</param>
/// <param name="DurationMs">Flight duration in milliseconds.</param>
/// <param name="Kind">Projectile kind (1 = Arcane, 2 = Fire, 3 = Spore).</param>
public readonly record struct ProjectileClientRow(
    float StartX,
    float StartY,
    float TargetX,
    float TargetY,
    long StartTime,
    float DurationMs,
    byte Kind);

/// <summary>
/// Client-side animated death poof / defeat particle effect (docs/adr/ADR-050).
/// </summary>
/// <param name="X">Tile X.</param>
/// <param name="Y">Tile Y.</param>
/// <param name="StartTime">Millisecond timestamp.</param>
/// <param name="DurationMs">Total lifetime of the poof.</param>
/// <param name="Kind">Poof visual row (0 = smoke, 1 = dust, 2 = explosion).</param>
public readonly record struct DeathEffectClientRow(
    int X,
    int Y,
    long StartTime,
    float DurationMs = 400f,
    byte Kind = 0);

/// <summary>
/// Client-side floating combat text (damage, crits, heals) rising above characters (docs/adr/ADR-055).
/// </summary>
/// <param name="WorldX">World pixel X.</param>
/// <param name="WorldY">World pixel Y.</param>
/// <param name="Amount">Combat numeric value.</param>
/// <param name="Style">0 = White, 1 = Gold/Crit, 2 = Red/DamageTaken, 3 = Green/Heal, 4 = Purple/Poison.</param>
/// <param name="StartTime">Millisecond timestamp.</param>
/// <param name="DurationMs">Total floating duration.</param>
public readonly record struct FloatingTextClientRow(
    int WorldX,
    int WorldY,
    int Amount,
    byte Style,
    long StartTime,
    float DurationMs = 650f);
