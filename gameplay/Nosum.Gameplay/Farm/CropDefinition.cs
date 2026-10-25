// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Farm;

/// <summary>
/// One kind of crop, as data: how long each growth stage takes while the soil is wet, and what
/// harvesting yields. Ids and keys are stable (docs/adr/ADR-011); durations are balance values and
/// may change between releases.
/// </summary>
public sealed class CropDefinition
{
    private readonly long[] _stageEndsMs;

    /// <summary>Initializes a crop.</summary>
    /// <param name="id">Stable numeric id, greater than zero.</param>
    /// <param name="key">Stable key, for example <c>parsnip</c>.</param>
    /// <param name="harvestItemId">Item the crop yields.</param>
    /// <param name="harvestQuantity">How many items one harvest yields.</param>
    /// <param name="stageSeconds">Seconds of watered growth each stage lasts, in order; the crop is mature after the last one.</param>
    public CropDefinition(int id, string key, int harvestItemId, int harvestQuantity, params int[] stageSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(harvestItemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(harvestQuantity);
        ArgumentNullException.ThrowIfNull(stageSeconds);
        ArgumentOutOfRangeException.ThrowIfZero(stageSeconds.Length);

        this.Id = id;
        this.Key = key;
        this.HarvestItemId = harvestItemId;
        this.HarvestQuantity = harvestQuantity;
        _stageEndsMs = new long[stageSeconds.Length];
        long total = 0;
        for (int i = 0; i < stageSeconds.Length; i++)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stageSeconds[i]);
            total += stageSeconds[i] * 1000L;
            _stageEndsMs[i] = total;
        }

        this.TotalGrowthMs = total;
    }

    /// <summary>Gets the stable numeric id.</summary>
    public int Id { get; }

    /// <summary>Gets the stable key.</summary>
    public string Key { get; }

    /// <summary>Gets the item one harvest yields.</summary>
    public int HarvestItemId { get; }

    /// <summary>Gets how many items one harvest yields.</summary>
    public int HarvestQuantity { get; }

    /// <summary>Gets the number of stages the crop shows: freshly planted (0) up to mature (<c>StageCount - 1</c>).</summary>
    public int StageCount => _stageEndsMs.Length + 1;

    /// <summary>Gets the watered growth time the crop needs to mature, milliseconds.</summary>
    public long TotalGrowthMs { get; }

    /// <summary>Returns the stage reached after some watered growth time.</summary>
    /// <param name="growthMs">Watered growth so far, milliseconds.</param>
    /// <returns>The stage, from 0 to <c>StageCount - 1</c> (mature).</returns>
    public int StageAt(long growthMs)
    {
        for (int i = 0; i < _stageEndsMs.Length; i++)
        {
            if (growthMs < _stageEndsMs[i])
            {
                return i;
            }
        }

        return _stageEndsMs.Length;
    }
}
