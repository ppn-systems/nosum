// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Combat;
using Nosum.Abstractions.Data;
using Nosum.Contracts.Player;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Skills;

namespace Nosum.Abstractions.Progress;

/// <summary>A player's energy and skill experience as stored (docs/adr/ADR-023).</summary>
/// <param name="Energy">Energy now, after the daily refill.</param>
/// <param name="MaxEnergy">Largest energy.</param>
/// <param name="FarmingXp">Cumulative farming experience.</param>
/// <param name="ForagingXp">Cumulative foraging experience.</param>
/// <param name="MiningXp">Cumulative mining experience.</param>
/// <param name="FishingXp">Cumulative fishing experience (docs/adr/ADR-071 D6).</param>
public readonly record struct PlayerProgressView(int Energy, int MaxEnergy, int FarmingXp, int ForagingXp, int MiningXp, int FishingXp = 0);

/// <summary>Result of <see cref="IPlayerProgress.TryChargeAsync"/>.</summary>
public enum ChargeStatus
{
    /// <summary>Charged and pushed.</summary>
    Ok = 0,

    /// <summary>The player lacks the energy; nothing changed.</summary>
    NoEnergy = 1,

    /// <summary>Another change got in the way; nothing changed, try again.</summary>
    Conflict = 2,
}

/// <summary>Result of <see cref="IPlayerProgress.EatAsync"/>.</summary>
/// <param name="Status">Outcome code.</param>
public readonly record struct EatOutcome(EatStatus Status);

/// <summary>
/// Per-player energy and skills (docs/adr/ADR-023). The stored truth is on the player's state row; every tool use charges it through
/// <see cref="ChargeAsync"/> in the same transaction as its effect (the one <c>TrySpend</c> of the farm and node paths).
/// </summary>
public interface IPlayerProgress
{
    /// <summary>Gets or sets the optional combat service used to apply potion effects when eating potion items.</summary>
    ICombatService? Combat
    {
        get => null;
        set { }
    }
    /// <summary>Reads a player's energy (refilled when a game day has passed) and experience.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The progress; a player with no record yet has full energy and no experience.</returns>
    Task<PlayerProgressView> GetAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Spends the energy of one successful tool use and adds its experience, in <paramref name="unit"/>. The caller saves the unit, calls
    /// <see cref="IProgressCharge.PublishAsync"/> and disposes the charge; if the unit is not saved nothing was charged.
    /// </summary>
    /// <param name="unit">The unit of work that carries the tool's effect.</param>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="tool">The tool used; sets the cost (and the discount of the tool's skill level).</param>
    /// <param name="skill">The skill that earns the experience.</param>
    /// <param name="xp">The experience to add.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The charge; check <see cref="IProgressCharge.Allowed"/>.</returns>
    Task<IProgressCharge> ChargeAsync(IUnitOfWork unit, Guid playerId, ToolKind tool, SkillKind skill, int xp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Spends a fixed amount of energy and adds experience, in <paramref name="unit"/>, for an action that is not a tool use (a fishing cast costs
    /// <see cref="Nosum.Gameplay.Fishing.FishingRules.CastEnergy"/>; a catch gives experience only, with <paramref name="energy"/> zero). Same rules as
    /// <see cref="ChargeAsync(IUnitOfWork, Guid, ToolKind, SkillKind, int, CancellationToken)"/>: the caller saves the unit, then publishes and disposes the charge.
    /// </summary>
    /// <param name="unit">The unit of work that carries the action's effect.</param>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="energy">Energy to spend; zero spends none and is always allowed.</param>
    /// <param name="skill">The skill that earns the experience.</param>
    /// <param name="xp">The experience to add.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The charge; check <see cref="IProgressCharge.Allowed"/>.</returns>
    Task<IProgressCharge> ChargeEnergyAsync(IUnitOfWork unit, Guid playerId, int energy, SkillKind skill, int xp, CancellationToken cancellationToken = default);

    /// <summary>Charges a tool use in a unit of work of its own, for effects that only change memory (a hit that does not break a node).</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="tool">The tool used.</param>
    /// <param name="skill">The skill that earns the experience.</param>
    /// <param name="xp">The experience to add.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Whether the tool use was charged.</returns>
    Task<ChargeStatus> TryChargeAsync(Guid playerId, ToolKind tool, SkillKind skill, int xp, CancellationToken cancellationToken = default);

    /// <summary>Eats one food item from a hotbar slot: removes it and restores its energy in one transaction.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="hotbarSlot">The hotbar slot.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome.</returns>
    Task<EatOutcome> EatAsync(Guid playerId, int hotbarSlot, CancellationToken cancellationToken = default);

    /// <summary>Pushes the player's energy and experience to them (after joining).</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="cancellationToken">Cancels the pushes.</param>
    /// <returns>A task that completes when the pushes were sent.</returns>
    Task PushAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Checks whether the player's current energy is below maximum.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the player's energy is below maximum.</returns>
    ValueTask<bool> IsEnergyBelowMaxAsync(Guid playerId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(false);

    /// <summary>Restores energy when a potion with food energy is consumed.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="foodEnergy">Food energy to restore.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when energy is restored and pushed.</returns>
    ValueTask RestoreEnergyFromPotionAsync(Guid playerId, int foodEnergy, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
