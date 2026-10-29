// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Mines;
using Nosum.Rendering;

namespace Nosum.Wasm.Game;

/// <summary>
/// The look of each mine biome (docs/adr/ADR-070 D2): a multiply tint over the floor's existing cave tiles, so every tier has its own palette
/// without a new sprite sheet. Earth draws the tiles unchanged. Pure; the scene passes the value to the tile renderer.
/// </summary>
internal static class MineBiomeLook
{
    private static readonly int s_frost = unchecked((int)0xB8D8FFFF);
    private static readonly int s_magma = unchecked((int)0xFFA878FF);
    private static readonly int s_ruins = unchecked((int)0xD8C890FF);

    /// <summary>Returns the multiply tint of a biome's tiles.</summary>
    /// <param name="biome">The biome.</param>
    /// <returns>A <c>0xRRGGBBAA</c> tint, or <see cref="RenderCommandBuffer.NoTint"/> for Earth.</returns>
    public static int TintFor(MineBiome biome) => biome switch
    {
        MineBiome.Frost => s_frost,
        MineBiome.Magma => s_magma,
        MineBiome.Ruins => s_ruins,
        _ => RenderCommandBuffer.NoTint,
    };

    /// <summary>Returns the tint of a map's tiles: the biome's tint on a mine floor, unchanged everywhere else.</summary>
    /// <param name="mineFloor">The map's mine floor, zero when it is not a mine floor.</param>
    /// <returns>A <c>0xRRGGBBAA</c> tint, or <see cref="RenderCommandBuffer.NoTint"/>.</returns>
    public static int TintForFloor(int mineFloor) => mineFloor >= 1 ? TintFor(MineBiomeRules.Of(mineFloor)) : RenderCommandBuffer.NoTint;
}
