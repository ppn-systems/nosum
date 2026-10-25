// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Farm;

namespace Nosum.Wasm.Game;

/// <summary>
/// Which picture a crop shows at each growth stage, cut out of <c>crops.png</c>: each crop is a row of
/// 16 x 32 frames. The frame lists have one entry per stage (<see cref="CropDefinition.StageCount"/>);
/// the first frames of a row show seeds in the ground, so a list may skip some. Placeholder art (docs/adr/ADR-007).
/// </summary>
internal static class CropSprites
{
    /// <summary>Width of one frame, pixels.</summary>
    public const int FrameWidth = 16;

    /// <summary>Height of one frame, pixels.</summary>
    public const int FrameHeight = 32;

    private static readonly CropSheetRow?[] s_rows = Build();

    /// <summary>Finds a crop's frame for a stage.</summary>
    /// <param name="cropId">Crop id.</param>
    /// <param name="stage">Growth stage, from 0.</param>
    /// <param name="sourceX">Left edge of the frame in the sheet.</param>
    /// <param name="sourceY">Top edge of the frame in the sheet.</param>
    /// <returns><see langword="true"/> when the crop has a picture.</returns>
    public static bool TryGetFrame(int cropId, int stage, out int sourceX, out int sourceY)
    {
        if ((uint)cropId < (uint)s_rows.Length && s_rows[cropId] is { } row && row.Frames.Length > 0)
        {
            int frame = row.Frames[Math.Clamp(stage, 0, row.Frames.Length - 1)];
            sourceX = row.X + (frame * FrameWidth);
            sourceY = row.Y;
            return true;
        }

        sourceX = 0;
        sourceY = 0;
        return false;
    }

    private static CropSheetRow?[] Build()
    {
        int highest = 0;
        foreach (CropDefinition crop in CropCatalog.All)
        {
            highest = Math.Max(highest, crop.Id);
        }

        CropSheetRow?[] rows = new CropSheetRow?[highest + 1];
        rows[CropCatalog.Parsnip] = new CropSheetRow(0, 0, [0, 2, 3, 4, 5]);
        rows[CropCatalog.Radish] = new CropSheetRow(128, 96, [0, 2, 3, 4, 6]);
        rows[CropCatalog.Cauliflower] = new CropSheetRow(0, 32, [0, 2, 3, 4, 5, 6]);
        foreach (CropDefinition crop in CropCatalog.All)
        {
            if (rows[crop.Id] is { } row && row.Frames.Length != crop.StageCount)
            {
                throw new InvalidOperationException($"Crop '{crop.Key}' has {crop.StageCount} stages but {row.Frames.Length} sprite frames.");
            }
        }

        return rows;
    }

    private sealed record CropSheetRow(int X, int Y, int[] Frames);
}
