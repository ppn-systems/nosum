// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;
using Nosum.Domain.Farm;

namespace Nosum.Domain.Buildings;

/// <summary>
/// A farm building (docs/adr/ADR-028): the extra state of a <see cref="PlacedObject"/> that was placed from a blueprint on the farm or co-op land. The placed object is
/// what stands on the land (tile, footprint); this row says what it is, when it is finished and how much feed its bench holds. Its <see cref="Entity.Id"/> names the interior instance.
/// </summary>
public sealed class FarmBuilding : Entity
{
    /// <summary>Gets or sets the placed object this building is, unique; deleting the object deletes the building.</summary>
    public int ObjectId { get; set; }

    /// <summary>Gets or sets the placed object; set when inserting so the key is filled in when the object is saved.</summary>
    public PlacedObject? Placed { get; set; }

    /// <summary>Gets or sets the map of the land it stands on (<c>farm</c> or <c>coopfarm</c>).</summary>
    public string MapId { get; set; } = string.Empty;

    /// <summary>Gets or sets the owner of the land: the player's id, or the co-op's id on co-op land.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Gets or sets the building kind (<c>BuildingKind</c> in Nosum.Gameplay).</summary>
    public byte Kind { get; set; }

    /// <summary>Gets or sets when the building is finished, Unix milliseconds on the server clock.</summary>
    public long CompletesAtMs { get; set; }

    /// <summary>Gets or sets the rations in the feeding bench after the last settlement.</summary>
    public int Rations { get; set; }

    /// <summary>Gets or sets the last game day the overnight feeding was settled for.</summary>
    public int SettledDay { get; set; }
}
