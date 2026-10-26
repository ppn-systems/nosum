// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Farm;

namespace Nosum.Wasm.Game;

/// <summary>
/// Which picture a crop shows at each growth stage, cut out of <c>crops.png</c>: each crop is a row of
/// 16 x 32 frames, two rows per 32 px band. The row and the frame of each stage are data of the crop
/// (<see cref="CropDefinition.SpriteRow"/>, <see cref="CropDefinition.SpriteFrames"/>, docs/adr/ADR-025 D6); this class only does the arithmetic,
/// so another crop is a catalogue entry and no code. Early art (docs/adr/ADR-007).
/// </summary>
internal static class CropSprites
{
    /// <summary>Width of one frame, pixels.</summary>
    public const int FrameWidth = 16;

    /// <summary>Height of one frame, pixels.</summary>
    public const int FrameHeight = 32;

    // Two crop rows share a 32 px band of the sheet: even rows start at x 0, odd rows at x 128.
    private const int RowWidth = 128;

    /// <summary>Finds a crop's frame for a stage.</summary>
    /// <param name="cropId">Crop id.</param>
    /// <param name="stage">Growth stage, from 0.</param>
    /// <param name="sourceX">Left edge of the frame in the sheet.</param>
    /// <param name="sourceY">Top edge of the frame in the sheet.</param>
    /// <returns><see langword="true"/> when the crop has a picture.</returns>
    public static bool TryGetFrame(int cropId, int stage, out int sourceX, out int sourceY)
    {
        if (CropCatalog.TryGet(cropId, out CropDefinition? crop) && crop.SpriteFrames.Count > 0)
        {
            int frame = crop.SpriteFrames[Math.Clamp(stage, 0, crop.SpriteFrames.Count - 1)];
            sourceX = (crop.SpriteRow % 2 * RowWidth) + (frame * FrameWidth);
            sourceY = crop.SpriteRow / 2 * FrameHeight;
            return true;
        }

        sourceX = 0;
        sourceY = 0;
        return false;
    }
}
