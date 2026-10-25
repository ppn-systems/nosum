// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Looks;

/// <summary>
/// What a player looks like, as ids only (docs/adr/ADR-006 D6, ADR-014): avatar parts chosen from <see cref="LookCatalog"/> and the
/// clothing items worn. An item id of zero means the player wears the plain default garment (or no hat). Clients turn the ids into
/// sheets, frames and tints; the server stores and validates them.
/// </summary>
/// <param name="SkinId">Skin tone id, 1 to <see cref="LookCatalog.SkinCount"/>.</param>
/// <param name="HairStyleId">Hair style id, 1 to <see cref="LookCatalog.HairStyleCount"/>.</param>
/// <param name="HairDyeId">Hair dye id, 1 to <see cref="LookCatalog.HairDyeCount"/>.</param>
/// <param name="ShirtItemId">Worn shirt item id, or zero for the plain shirt.</param>
/// <param name="PantsItemId">Worn trousers item id, or zero for the plain trousers.</param>
/// <param name="HatItemId">Worn hat item id, or zero for no hat.</param>
public readonly record struct PlayerLook(int SkinId, int HairStyleId, int HairDyeId, int ShirtItemId, int PantsItemId, int HatItemId)
{
    /// <summary>Gets the look every new player and every guest starts with.</summary>
    public static PlayerLook Default => new(1, 1, 1, 0, 0, 0);

    /// <summary>Returns the worn item id of a slot.</summary>
    /// <param name="slot">Where.</param>
    /// <returns>The item id, or zero.</returns>
    public int Worn(WearSlot slot) => slot switch
    {
        WearSlot.Shirt => this.ShirtItemId,
        WearSlot.Pants => this.PantsItemId,
        WearSlot.Hat => this.HatItemId,
        _ => 0,
    };

    /// <summary>Returns a copy with another item worn in a slot.</summary>
    /// <param name="slot">Where.</param>
    /// <param name="itemId">The item id, or zero for the default.</param>
    /// <returns>The new look.</returns>
    public PlayerLook With(WearSlot slot, int itemId) => slot switch
    {
        WearSlot.Shirt => this with { ShirtItemId = itemId },
        WearSlot.Pants => this with { PantsItemId = itemId },
        WearSlot.Hat => this with { HatItemId = itemId },
        _ => this,
    };
}
