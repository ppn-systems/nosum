// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Pets;

/// <summary>
/// The pet companion of one farmer (docs/adr/ADR-075 D3-D4): a cat or a dog with a name, a mood and the game day it was last petted.
/// One row at most per player; the row is removed only with the player.
/// </summary>
public sealed class PlayerPet
{
    /// <summary>Gets or sets the owning player; also this row's primary key.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the kind: 1 cat, 2 dog (<c>PetKind</c>).</summary>
    public byte Kind { get; set; }

    /// <summary>Gets or sets the name, cleaned and masked when it was given.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the mood, 0 to 100.</summary>
    public int Mood { get; set; }

    /// <summary>Gets or sets the game day the pet was last petted; 0 when never.</summary>
    public long LastPettedDay { get; set; }

    /// <summary>Gets or sets when the pet was adopted.</summary>
    public DateTimeOffset AdoptedAt { get; set; }
}
