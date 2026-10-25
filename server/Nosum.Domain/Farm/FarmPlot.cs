// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Farm;

/// <summary>
/// One tilled tile of a map. The row exists once the soil is tilled; an untilled tile has no row.
/// The key is (map, owner, x, y): the farm is private per player (docs/adr/ADR-015), so the owner is part of the key. Times are Unix milliseconds on the server clock, the same values
/// <c>Nosum.Gameplay.Farm.FarmTile</c> computes growth from.
/// </summary>
public sealed class FarmPlot
{
    /// <summary>Gets or sets the map key, for example <c>farm</c>.</summary>
    public string MapId { get; set; } = string.Empty;

    /// <summary>Gets or sets the owner of the instance the tile is in; <see cref="Guid.Empty"/> for a shared map.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Gets or sets the tile x.</summary>
    public int X { get; set; }

    /// <summary>Gets or sets the tile y.</summary>
    public int Y { get; set; }

    /// <summary>Gets or sets the planted crop id; zero for bare soil.</summary>
    public int CropId { get; set; }

    /// <summary>Gets or sets when the crop was planted; zero without a crop.</summary>
    public long PlantedAtMs { get; set; }

    /// <summary>Gets or sets the watered growth time settled up to <see cref="SettledAtMs"/>.</summary>
    public long GrowthMs { get; set; }

    /// <summary>Gets or sets the moment <see cref="GrowthMs"/> was last settled.</summary>
    public long SettledAtMs { get; set; }

    /// <summary>Gets or sets the moment the soil stops being wet.</summary>
    public long WetUntilMs { get; set; }

    /// <summary>Gets or sets the player who last worked the tile (tilled, planted, watered or harvested it); null when unknown.</summary>
    public Guid? LastWorkedBy { get; set; }

    /// <summary>Gets or sets when the row last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
