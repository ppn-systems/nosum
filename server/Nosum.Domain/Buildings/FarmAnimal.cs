// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Buildings;

/// <summary>An animal living in a <see cref="FarmBuilding"/> (docs/adr/ADR-028 D3). Days are game day numbers; zero means never.</summary>
public sealed class FarmAnimal : Entity
{
    /// <summary>Gets or sets the building it lives in.</summary>
    public Guid BuildingId { get; set; }

    /// <summary>Gets or sets the species (<c>AnimalKind</c> in Nosum.Gameplay).</summary>
    public byte Kind { get; set; }

    /// <summary>Gets or sets the name its owner gave it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the friendship, 0 to 1000.</summary>
    public int Friendship { get; set; }

    /// <summary>Gets or sets the game day it was last petted.</summary>
    public int PettedDay { get; set; }

    /// <summary>Gets or sets the game day it last ate at the start of, and so had produce.</summary>
    public int ProduceDay { get; set; }

    /// <summary>Gets or sets the game day its produce was last collected.</summary>
    public int CollectedDay { get; set; }

    /// <summary>Gets or sets when it was bought.</summary>
    public DateTimeOffset BoughtAt { get; set; }
}
