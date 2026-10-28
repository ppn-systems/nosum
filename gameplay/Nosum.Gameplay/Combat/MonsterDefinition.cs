// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Combat;

/// <summary>Static combat stats and loot table for a <see cref="MonsterKind"/> (docs/adr/ADR-045 D3).</summary>
public sealed class MonsterDefinition
{
    /// <summary>Initializes a new instance of the <see cref="MonsterDefinition"/> class.</summary>
    /// <param name="kind">The monster species.</param>
    /// <param name="key">Stable snake_case key used for localization.</param>
    /// <param name="maxHp">Maximum health points.</param>
    /// <param name="attackDamage">Damage dealt to a player on counter-attack.</param>
    /// <param name="xpReward">Combat experience awarded when defeated.</param>
    /// <param name="primaryDropItemId">Primary item dropped on defeat.</param>
    /// <param name="rareDropItemId">Secondary/rare item dropped on defeat.</param>
    public MonsterDefinition(
        MonsterKind kind,
        string key,
        int maxHp,
        int attackDamage,
        int xpReward,
        int primaryDropItemId,
        int rareDropItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxHp, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(attackDamage, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(xpReward, 0);

        Kind = kind;
        Key = key;
        MaxHp = maxHp;
        AttackDamage = attackDamage;
        XpReward = xpReward;
        PrimaryDropItemId = primaryDropItemId;
        RareDropItemId = rareDropItemId;
    }

    /// <summary>Gets the monster species.</summary>
    public MonsterKind Kind { get; }

    /// <summary>Gets the stable snake_case key.</summary>
    public string Key { get; }

    /// <summary>Gets the maximum HP of the monster.</summary>
    public int MaxHp { get; }

    /// <summary>Gets the counter-attack damage dealt to a player.</summary>
    public int AttackDamage { get; }

    /// <summary>Gets the combat experience granted on defeat.</summary>
    public int XpReward { get; }

    /// <summary>Gets the guaranteed drop item id.</summary>
    public int PrimaryDropItemId { get; }

    /// <summary>Gets the bonus drop item id.</summary>
    public int RareDropItemId { get; }
}
