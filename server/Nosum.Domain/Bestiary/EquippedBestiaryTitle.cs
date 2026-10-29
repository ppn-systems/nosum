// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Combat;

namespace Nosum.Domain.Bestiary;

/// <summary>
/// The slayer title a player wears: at most one row per player, present only while a title is equipped (docs/adr/ADR-067). The title is the
/// species' own, so it is valid only once the species' last milestone is reached.
/// </summary>
public sealed class EquippedBestiaryTitle
{
    /// <summary>Gets or sets the player; also this row's primary key.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the species whose title is equipped; never <see cref="MonsterKind.None"/>.</summary>
    public MonsterKind Species { get; set; }
}
