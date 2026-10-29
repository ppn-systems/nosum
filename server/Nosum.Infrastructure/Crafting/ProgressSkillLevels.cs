// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Combat;
using Nosum.Abstractions.Progress;
using Nosum.Abstractions.Skills;
using Nosum.Gameplay.Crafting;
using Nosum.Gameplay.Skills;

namespace Nosum.Infrastructure.Crafting;

/// <summary>
/// <see cref="ISkillLevels"/> read from the player's experience (docs/adr/ADR-023 D3): recipe unlocks (docs/adr/ADR-024 D1) follow the levels the
/// player earned farming, foraging, mining and fighting monsters.
/// </summary>
public sealed class ProgressSkillLevels : ISkillLevels
{
    private readonly IPlayerProgress _progress;
    private readonly ICombatService? _combat;

    /// <summary>Initializes the reader.</summary>
    /// <param name="progress">The player's energy and experience.</param>
    /// <param name="combat">Optional combat service for <see cref="CraftSkill.Combat"/> levels.</param>
    public ProgressSkillLevels(IPlayerProgress progress, ICombatService? combat = null)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _progress = progress;
        _combat = combat;
    }

    /// <inheritdoc/>
    public async ValueTask<int> LevelOfAsync(Guid playerId, CraftSkill skill, CancellationToken cancellationToken = default)
    {
        if (skill == CraftSkill.None)
        {
            return SkillRules.LevelOf(long.MaxValue);
        }

        if (skill == CraftSkill.Combat)
        {
            if (_combat is null)
            {
                return 0;
            }

            VitalsView vitals = await _combat.GetVitalsAsync(playerId, cancellationToken).ConfigureAwait(false);
            return SkillRules.LevelOf(vitals.CombatXp);
        }

        PlayerProgressView view = await _progress.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        long xp = skill switch
        {
            CraftSkill.Farming => view.FarmingXp,
            CraftSkill.Foraging => view.ForagingXp,
            CraftSkill.Mining => view.MiningXp,
            CraftSkill.Fishing => view.FishingXp,
            _ => 0,
        };

        return SkillRules.LevelOf(xp);
    }
}
