// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Gameplay.Crafting;

namespace Nosum.Abstractions.Skills;

/// <summary>
/// A player's skill levels, as far as recipes need them (docs/adr/ADR-024 D1). The energy and skills work (docs/adr/ADR-023) owns the real thing: levels
/// derived from the XP stored on the player's state. Until it lands, <c>FixedSkillLevels</c> answers level 10 for every skill, so every recipe is unlocked.
/// </summary>
public interface ISkillLevels
{
    /// <summary>Reads a player's level in a skill.</summary>
    /// <param name="playerId">The account id.</param>
    /// <param name="skill">The skill.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The level, 0 to 10.</returns>
    ValueTask<int> LevelOfAsync(Guid playerId, CraftSkill skill, CancellationToken cancellationToken = default);
}
