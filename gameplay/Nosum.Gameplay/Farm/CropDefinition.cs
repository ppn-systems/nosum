// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Calendar;

namespace Nosum.Gameplay.Farm;

/// <summary>
/// One kind of crop, as data: how many watered game days each growth stage takes (docs/adr/ADR-021 D7), and what
/// harvesting yields. Ids and keys are stable (docs/adr/ADR-011); durations are balance values and
/// may change between releases.
/// </summary>
public sealed class CropDefinition
{
    private readonly int[] _stageEndDays;

    /// <summary>Initializes a crop.</summary>
    /// <param name="id">Stable numeric id, greater than zero.</param>
    /// <param name="key">Stable key, for example <c>parsnip</c>.</param>
    /// <param name="seasons">The seasons the crop grows in; planting is refused in any other season and the crop withers when one begins (docs/adr/ADR-018).</param>
    /// <param name="harvestItemId">Item the crop yields.</param>
    /// <param name="harvestQuantity">How many items one harvest yields.</param>
    /// <param name="stageDays">Watered game days each stage lasts, in order; the crop is mature after the last one.</param>
    public CropDefinition(int id, string key, SeasonSet seasons, int harvestItemId, int harvestQuantity, params int[] stageDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfEqual((int)seasons, (int)SeasonSet.None);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(harvestItemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(harvestQuantity);
        ArgumentNullException.ThrowIfNull(stageDays);
        ArgumentOutOfRangeException.ThrowIfZero(stageDays.Length);

        this.Id = id;
        this.Key = key;
        this.Seasons = seasons;
        this.HarvestItemId = harvestItemId;
        this.HarvestQuantity = harvestQuantity;
        _stageEndDays = new int[stageDays.Length];
        int total = 0;
        for (int i = 0; i < stageDays.Length; i++)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stageDays[i]);
            total += stageDays[i];
            _stageEndDays[i] = total;
        }

        this.TotalGrowthDays = total;
    }

    /// <summary>Gets the stable numeric id.</summary>
    public int Id { get; }

    /// <summary>Gets the stable key.</summary>
    public string Key { get; }

    /// <summary>Gets the seasons the crop grows in.</summary>
    public SeasonSet Seasons { get; }

    /// <summary>Gets the item one harvest yields.</summary>
    public int HarvestItemId { get; }

    /// <summary>Gets how many items one harvest yields.</summary>
    public int HarvestQuantity { get; }

    /// <summary>Gets the number of stages the crop shows: freshly planted (0) up to mature (<c>StageCount - 1</c>).</summary>
    public int StageCount => _stageEndDays.Length + 1;

    /// <summary>Gets the watered game days the crop needs to mature.</summary>
    public int TotalGrowthDays { get; }

    /// <summary>
    /// Gets the watered game days a regrowing crop needs to bear again after a harvest (docs/adr/ADR-025 D2); zero for a crop that is
    /// gone after one harvest. Never more than the days of the last stage, so the plant returns to its last growing stage.
    /// </summary>
    public int RegrowDays { get; init; }

    /// <summary>Gets whether a harvest leaves the plant standing to bear again.</summary>
    public bool Regrows => this.RegrowDays > 0;

    /// <summary>Gets the watered game days a plant has already, right after a harvest, when it regrows: it needs <see cref="RegrowDays"/> more.</summary>
    public int GrowthDaysAfterHarvest => this.Regrows ? this.TotalGrowthDays - this.RegrowDays : 0;

    /// <summary>Gets the row of <c>assets/sprites/crops.png</c> the crop's pictures are in: two rows per 32 px band, 128 px each (docs/adr/ADR-025 D6).</summary>
    public int SpriteRow { get; init; }

    /// <summary>Gets the frame of the sheet row (16 × 32 px, eight per row) shown at each stage, <see cref="StageCount"/> entries; the client reads no other crop art data.</summary>
    public IReadOnlyList<int> SpriteFrames { get; init; } = [];

    /// <summary>Returns whether the crop can be planted, and grows, in a season.</summary>
    /// <param name="season">The season.</param>
    /// <returns><see langword="true"/> when the crop grows in the season.</returns>
    public bool GrowsIn(Season season) => this.Seasons.Contains(season);

    /// <summary>Returns the stage reached after some watered game days.</summary>
    /// <param name="growthDays">Watered game days so far.</param>
    /// <returns>The stage, from 0 to <c>StageCount - 1</c> (mature).</returns>
    public int StageAt(int growthDays)
    {
        for (int i = 0; i < _stageEndDays.Length; i++)
        {
            if (growthDays < _stageEndDays[i])
            {
                return i;
            }
        }

        return _stageEndDays.Length;
    }
}
