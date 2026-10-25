// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Farm;

/// <summary>
/// The state of one tilled tile. A tile that was never tilled (or whose bare soil has reverted) has no <see cref="FarmTile"/>.
/// Growth counts watered game days (docs/adr/ADR-021 D7) and is computed on demand from three stored numbers, never simulated:
/// a day number is the 1-based count of game days since the calendar epoch (<c>GameCalendar.DayNumberAt</c>), and zero means "never".
/// <para>
/// Representation: <paramref name="GrowthDays"/> holds the watered days that are already settled; the day in <paramref name="WateredDay"/> is
/// not part of it. When that day has ended (the day number now is greater) it adds one more day on read, and the next watering settles it into
/// <paramref name="GrowthDays"/>. So a tile watered on day N grows at the rollover into day N + 1, watering twice on one day counts once, and a
/// day without watering adds nothing.
/// </para>
/// </summary>
/// <param name="CropId">The planted crop, or zero for bare soil.</param>
/// <param name="PlantedAtMs">When the crop was planted, Unix milliseconds on the server's clock (withering is decided by it, docs/adr/ADR-018); zero without a crop.</param>
/// <param name="GrowthDays">Watered game days settled so far, not counting <paramref name="WateredDay"/>.</param>
/// <param name="WateredDay">The last game day the soil was watered; the soil is wet while that is the current day. Zero when never.</param>
/// <param name="TendedDay">The game day bare soil was tilled or its crop harvested; with <paramref name="WateredDay"/> it starts the dry days that may revert bare soil. Zero with a crop.</param>
public readonly record struct FarmTile(int CropId, long PlantedAtMs, int GrowthDays, int WateredDay, int TendedDay)
{
    /// <summary>Gets bare, dry soil with no history.</summary>
    public static FarmTile Bare => default;

    /// <summary>Gets whether a crop grows on the tile.</summary>
    public bool HasCrop => this.CropId != 0;
}
