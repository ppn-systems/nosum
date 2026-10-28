// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Combat;
using Nosum.Gameplay.Combat;

namespace Nosum.Abstractions.Combat;

/// <summary>Snapshot of a player's HP, MP and Combat XP (docs/adr/ADR-045 D2).</summary>
/// <param name="Hp">Current HP.</param>
/// <param name="MaxHp">Maximum HP.</param>
/// <param name="Mp">Current MP.</param>
/// <param name="MaxMp">Maximum MP.</param>
/// <param name="CombatXp">Cumulative Combat experience.</param>
/// <param name="StatusEffect">Active status effect kind (docs/adr/ADR-051).</param>
public readonly record struct VitalsView(int Hp, int MaxHp, int Mp, int MaxMp, int CombatXp, byte StatusEffect = 0);

/// <summary>Snapshot of the monsters and environmental hazards on a map or mine floor (docs/adr/ADR-061).</summary>
/// <param name="MapId">The map id.</param>
/// <param name="Monsters">Live monsters on the map.</param>
/// <param name="Hazards">Environmental hazards on the map.</param>
public sealed record MonstersView(string MapId, IReadOnlyList<MonsterSpawn> Monsters, IReadOnlyList<DungeonHazard>? Hazards = null)
{
    /// <summary>Environmental hazards on the map.</summary>
    public IReadOnlyList<DungeonHazard> Hazards { get; init; } = Hazards ?? [];
}

/// <summary>Outcome of attacking a monster.</summary>
/// <param name="Status">Combat status.</param>
/// <param name="MonsterId">Target monster id.</param>
/// <param name="DamageDealt">Damage dealt to the monster.</param>
/// <param name="MonsterHp">Remaining monster HP (0 when defeated).</param>
/// <param name="DamageTaken">Counter-attack damage taken by the player.</param>
/// <param name="Vitals">Updated player vitals.</param>
/// <param name="KnockedOut">Whether the player was knocked out and revived.</param>
/// <param name="ComboCount">Active combat combo streak count (docs/adr/ADR-060).</param>
public readonly record struct AttackOutcome(
    CombatStatus Status,
    int MonsterId,
    int DamageDealt,
    int MonsterHp,
    int DamageTaken,
    VitalsView Vitals,
    bool KnockedOut,
    byte ComboCount = 0);

/// <summary>Outcome of drinking a potion.</summary>
/// <param name="Status">Combat status.</param>
/// <param name="Vitals">Updated player vitals.</param>
public readonly record struct DrinkPotionOutcome(CombatStatus Status, VitalsView Vitals);

/// <summary>Outcome of executing a special combat skill (docs/adr/ADR-056).</summary>
/// <param name="Status">Combat status outcome.</param>
/// <param name="SkillId">Skill kind.</param>
/// <param name="DamageDealt">Total damage dealt.</param>
/// <param name="TargetsHit">Count of monsters struck.</param>
/// <param name="HealAmount">Health points restored.</param>
/// <param name="Vitals">Updated player vitals.</param>
/// <param name="CooldownMs">Skill cooldown duration.</param>
/// <param name="ComboCount">Active combat combo streak count (docs/adr/ADR-060).</param>
public readonly record struct CastSkillOutcome(
    CombatStatus Status,
    byte SkillId,
    int DamageDealt,
    byte TargetsHit,
    int HealAmount,
    VitalsView Vitals,
    int CooldownMs,
    byte ComboCount = 0);

/// <summary>
/// Server-authoritative MMORPG vitals (HP &amp; MP), monster encounters, weapon/staff attacks, and potion drinking
/// (docs/adr/ADR-045 D3).
/// </summary>
public interface ICombatService
{
    /// <summary>Gets a player's current HP, MP and Combat XP.</summary>
    /// <param name="playerId">Player account id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The player's vitals.</returns>
    ValueTask<VitalsView> GetVitalsAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Gets the live monsters on the player's current map or mine floor.</summary>
    /// <param name="connection">Player connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The map's monster roster.</returns>
    ValueTask<MonstersView> GetMonstersAsync(IConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Attacks a monster on the player's current map with the weapon/staff in <paramref name="slot"/>.</summary>
    /// <param name="connection">Player connection.</param>
    /// <param name="monsterId">Target monster id.</param>
    /// <param name="slot">Hotbar/inventory slot holding the weapon or staff.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attack outcome.</returns>
    Task<AttackOutcome> AttackMonsterAsync(IConnection connection, int monsterId, int slot, CancellationToken cancellationToken = default);

    /// <summary>Drinks a potion in <paramref name="slot"/> to restore HP and MP.</summary>
    /// <param name="connection">Player connection.</param>
    /// <param name="slot">Hotbar/inventory slot holding the potion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The potion outcome.</returns>
    Task<DrinkPotionOutcome> DrinkPotionAsync(IConnection connection, int slot, CancellationToken cancellationToken = default);

    /// <summary>Casts a special combat skill (docs/adr/ADR-056).</summary>
    /// <param name="connection">Player connection.</param>
    /// <param name="skillId">Skill kind.</param>
    /// <param name="targetMonsterId">Target monster id for targeted skills.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The skill cast outcome.</returns>
    Task<CastSkillOutcome> CastSkillAsync(IConnection connection, byte skillId, int targetMonsterId, CancellationToken cancellationToken = default);
}
