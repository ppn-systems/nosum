// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Domain.Placeables;

/// <summary>
/// The smelting job of a furnace: which ore went in and when. One row per furnace that is smelting or holds a finished bar; collecting the bar
/// deletes the row. Whether the bar is ready is computed from <see cref="StartedAtMs"/>, the server clock and the calendar, never stored
/// (docs/adr/ADR-024 D4). The row goes with the furnace.
/// </summary>
public sealed class FurnaceJob
{
    /// <summary>Gets or sets the id of the placed furnace; also this row's primary key (one job per furnace).</summary>
    public int ObjectId { get; set; }

    /// <summary>Gets or sets the ore smelted: copper ore or iron ore.</summary>
    public int OreItemId { get; set; }

    /// <summary>Gets or sets when the job started, Unix milliseconds on the server clock.</summary>
    public long StartedAtMs { get; set; }
}
