// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.Socket.Look;

/// <summary>
/// A look change the player chose that the server has not answered for yet. The wardrobe shows it on top of the look the server sent, so the choice
/// is visible at once; a refused change drops out when its answer arrives, and the server's look shows again (docs/adr/ADR-120).
/// </summary>
public abstract record PendingLook
{
    /// <summary>Shows this change on a look.</summary>
    /// <param name="look">The look: the server's, with the changes before this one applied.</param>
    /// <returns>The look as it would be once the server accepts this change.</returns>
    public abstract LookInfo Apply(LookInfo look);

    /// <summary>Sets what is worn in one wear slot (1 shirt, 2 trousers, 3 hat, 4 boots, as <c>WearSlot</c> numbers them).</summary>
    /// <param name="look">The look to change.</param>
    /// <param name="wearSlot">The wear slot.</param>
    /// <param name="itemId">The item worn there, or zero.</param>
    /// <returns>The changed look; the look itself when the slot is unknown.</returns>
    protected static LookInfo WithWorn(LookInfo look, int wearSlot, int itemId) => wearSlot switch
    {
        1 => look with { ShirtItemId = itemId },
        2 => look with { PantsItemId = itemId },
        3 => look with { HatItemId = itemId },
        4 => look with { FeetItemId = itemId },
        _ => look,
    };
}

/// <summary>A skin, hair style and hair dye choice. The server takes all three in one request, so each request carries the whole appearance.</summary>
/// <param name="SkinId">Skin tone id.</param>
/// <param name="HairStyleId">Hair style id.</param>
/// <param name="HairDyeId">Hair dye id.</param>
public sealed record PendingAppearance(int SkinId, int HairStyleId, int HairDyeId) : PendingLook
{
    /// <inheritdoc/>
    public override LookInfo Apply(LookInfo look) => look with { SkinId = this.SkinId, HairStyleId = this.HairStyleId, HairDyeId = this.HairDyeId };
}

/// <summary>Wearing a clothing item from an inventory slot.</summary>
/// <param name="InventorySlot">The inventory slot the item is in.</param>
/// <param name="WearSlot">The wear slot the item goes to.</param>
/// <param name="ItemId">The item.</param>
public sealed record PendingWear(int InventorySlot, int WearSlot, int ItemId) : PendingLook
{
    /// <inheritdoc/>
    public override LookInfo Apply(LookInfo look) => WithWorn(look, this.WearSlot, this.ItemId);
}

/// <summary>Taking the item off a wear slot.</summary>
/// <param name="WearSlot">The wear slot.</param>
public sealed record PendingTakeOff(int WearSlot) : PendingLook
{
    /// <inheritdoc/>
    public override LookInfo Apply(LookInfo look) => WithWorn(look, this.WearSlot, 0);
}
