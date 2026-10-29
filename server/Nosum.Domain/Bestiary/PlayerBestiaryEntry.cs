// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Combat;

namespace Nosum.Domain.Bestiary;

/// <summary>
/// How many monsters of one species a player has slain: a row of the hunting log (docs/adr/ADR-068). The key is the player and the species, so one
/// species has one count per player.
/// </summary>
public sealed class PlayerBestiaryEntry
{
    /// <summary>Gets or sets the player who slew the monsters.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the monster species.</summary>
    public MonsterKind Species { get; set; }

    /// <summary>Gets or sets how many monsters of the species the player has slain.</summary>
    public int Kills { get; set; }

    /// <summary>Gets or sets when the row last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
