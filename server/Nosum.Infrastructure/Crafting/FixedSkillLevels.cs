// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Skills;
using Nosum.Gameplay.Crafting;

namespace Nosum.Infrastructure.Crafting;

/// <summary>
/// <see cref="ISkillLevels"/> until the skills of docs/adr/ADR-023 exist: every player has level 10 in every skill, so every recipe is unlocked.
/// Replace it in the host with the skills service when that work lands; nothing else changes.
/// </summary>
public sealed class FixedSkillLevels : ISkillLevels
{
    /// <summary>The level every player has in every skill.</summary>
    public const int Level = 10;

    /// <inheritdoc/>
    public ValueTask<int> LevelOfAsync(Guid playerId, CraftSkill skill, CancellationToken cancellationToken = default) => ValueTask.FromResult(Level);
}
