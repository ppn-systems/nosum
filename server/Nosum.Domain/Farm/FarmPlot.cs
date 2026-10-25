// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Farm;

/// <summary>
/// One tilled tile of a map. The row exists once the soil is tilled; an untilled tile has no row.
/// The key is (map, owner, x, y): the farm is private per player (docs/adr/ADR-015), so the owner is part of the key. Times are Unix milliseconds on the server clock and days are game day numbers, the same values
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

    /// <summary>Gets or sets the watered game days settled so far, not counting <see cref="WateredDay"/> (docs/adr/ADR-021 D7).</summary>
    public int GrowthDays { get; set; }

    /// <summary>Gets or sets the last game day number (1-based since the calendar epoch) the soil was watered; zero when never.</summary>
    public int WateredDay { get; set; }

    /// <summary>Gets or sets the game day bare soil was tilled or its crop harvested; zero with a crop or for a row from before day counting.</summary>
    public int TendedDay { get; set; }

    /// <summary>Gets or sets the player who last worked the tile (tilled, planted, watered or harvested it); null when unknown.</summary>
    public Guid? LastWorkedBy { get; set; }

    /// <summary>Gets or sets when the row last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
