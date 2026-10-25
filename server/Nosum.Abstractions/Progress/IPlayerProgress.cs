// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
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
public readonly record struct PlayerProgressView(int Energy, int MaxEnergy, int FarmingXp, int ForagingXp, int MiningXp);

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
/// Energy and experience of one tool use, charged inside the unit of work that carries the tool's effect. Holds the player's progress lock until it is disposed,
/// so dispose it after the unit saved (or after the effect was refused).
/// </summary>
public interface IProgressCharge : IDisposable
{
    /// <summary>Gets whether the player had the energy. When <see langword="false"/> nothing was changed and the tool use must be refused.</summary>
    bool Allowed { get; }

    /// <summary>Tells the player the new energy, experience and any level reached. Call it after the unit of work was saved.</summary>
    /// <param name="cancellationToken">Cancels the pushes.</param>
    /// <returns>A task that completes when the pushes were sent.</returns>
    ValueTask PublishAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Per-player energy and skills (docs/adr/ADR-023). The stored truth is on the player's state row; every tool use charges it through
/// <see cref="ChargeAsync"/> in the same transaction as its effect (the one <c>TrySpend</c> of the farm and node paths).
/// </summary>
public interface IPlayerProgress
{
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
}
