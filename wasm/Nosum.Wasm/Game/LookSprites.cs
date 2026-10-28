// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.ViewModels.Looks;

namespace Nosum.Wasm.Game;

/// <summary>
/// Resolves the ids the server sent for a player's look (skin, hair, worn items) into the sheet frames and tints <see cref="FarmerSprite"/>
/// draws, through the client's <see cref="LookPalette"/> catalogue (docs/adr/ADR-006 D6). Nothing here depends on a player id.
/// </summary>
internal static class LookSprites
{
    /// <summary>Resolves a look.</summary>
    /// <param name="look">The ids from the server.</param>
    /// <returns>What to draw.</returns>
    public static FarmerLook Resolve(in LookInfo look)
    {
        ClothingSprite shirt = LookPalette.Shirt(look.ShirtItemId);
        ClothingSprite pants = LookPalette.Pants(look.PantsItemId);
        int hat = -1;
        int hatTint = unchecked((int)0xFFFFFFFF);
        if (LookPalette.TryHat(look.HatItemId, out ClothingSprite hatSprite))
        {
            hat = hatSprite.SheetIndex;
            hatTint = hatSprite.Tint;
        }

        return new FarmerLook(
            Hairstyle: LookPalette.HairFrame(look.HairStyleId),
            HairTint: LookPalette.DyeTint(look.HairDyeId),
            Shirt: shirt.SheetIndex,
            Pants: pants.SheetIndex,
            PantsTint: pants.Tint,
            SkinTint: LookPalette.SkinTint(look.SkinId),
            Hat: hat,
            HatTint: hatTint,
            ShirtTint: shirt.Tint);
    }
}
