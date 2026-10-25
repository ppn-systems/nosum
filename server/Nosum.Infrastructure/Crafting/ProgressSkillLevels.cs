// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Progress;
using Nosum.Abstractions.Skills;
using Nosum.Gameplay.Crafting;
using Nosum.Gameplay.Skills;

namespace Nosum.Infrastructure.Crafting;

/// <summary>
/// <see cref="ISkillLevels"/> read from the player's experience (docs/adr/ADR-023 D3): recipe unlocks (docs/adr/ADR-024 D1) follow the levels the
/// player earned farming, foraging and mining.
/// </summary>
public sealed class ProgressSkillLevels : ISkillLevels
{
    private readonly IPlayerProgress _progress;

    /// <summary>Initializes the reader.</summary>
    /// <param name="progress">The player's energy and experience.</param>
    public ProgressSkillLevels(IPlayerProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _progress = progress;
    }

    /// <inheritdoc/>
    public async ValueTask<int> LevelOfAsync(Guid playerId, CraftSkill skill, CancellationToken cancellationToken = default)
    {
        if (skill == CraftSkill.None)
        {
            return SkillRules.LevelOf(long.MaxValue);
        }

        PlayerProgressView view = await _progress.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        long xp = skill switch
        {
            CraftSkill.Farming => view.FarmingXp,
            CraftSkill.Foraging => view.ForagingXp,
            CraftSkill.Mining => view.MiningXp,
            _ => 0,
        };

        return SkillRules.LevelOf(xp);
    }
}
