// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Look;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Looks;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Looks;

/// <summary>One choice of an avatar part: its id, the colour to show on the button (empty for a style) and whether it is the current one.</summary>
/// <param name="Id">The part id sent to the server.</param>
/// <param name="Css">A CSS colour for a swatch, or an empty string.</param>
/// <param name="Label">The localized label.</param>
/// <param name="IsCurrent">Whether the player has it now.</param>
public sealed record PartChoice(int Id, string Css, string Label, bool IsCurrent);

/// <summary>One worn item (or the plain default) of the wardrobe.</summary>
/// <param name="Slot">Where it is worn (1 shirt, 2 trousers, 3 hat).</param>
/// <param name="SlotLabel">The localized slot name.</param>
/// <param name="Item">The worn stack, empty when nothing is worn.</param>
public sealed record WornDisplay(int Slot, string SlotLabel, SlotDisplay Item);

/// <summary>One layer of the character preview: a window onto a farmer sheet, drawn in order, optionally tinted.</summary>
/// <param name="SheetUrl">URL of the sheet, relative to the app root.</param>
/// <param name="X">Left edge of the frame in the sheet, pixels.</param>
/// <param name="Y">Top edge of the frame in the sheet, pixels.</param>
/// <param name="Width">Frame width, pixels.</param>
/// <param name="Height">Frame height, pixels.</param>
/// <param name="SheetWidth">Width of the whole sheet, pixels.</param>
/// <param name="SheetHeight">Height of the whole sheet, pixels.</param>
/// <param name="OffsetX">Where the frame sits inside the 16 x 32 character box, pixels.</param>
/// <param name="OffsetY">Where the frame sits inside the 16 x 32 character box, pixels.</param>
/// <param name="Tint">A CSS colour multiplied onto the frame, or <see langword="null"/> for none.</param>
public sealed record PreviewLayer(string SheetUrl, int X, int Y, int Width, int Height, int SheetWidth, int SheetHeight, int OffsetX, int OffsetY, string? Tint);

/// <summary>
/// View model of the wardrobe panel: choose skin, hair style and dye, wear clothing from the inventory and take it off. The server
/// validates everything and answers with the new look; the panel shows what the server last sent.
/// </summary>
public sealed class WardrobeViewModel : PanelViewModel, IDisposable
{
    private readonly LookChannel _looks;
    private readonly InventoryViewModel _inventory;
    private readonly ItemDescriber _describer;

    /// <summary>Initializes the view model.</summary>
    /// <param name="looks">The look channel.</param>
    /// <param name="inventory">The inventory view model (the clothing that can be worn).</param>
    /// <param name="describer">Describes worn items.</param>
    /// <param name="text">UI text.</param>
    public WardrobeViewModel(LookChannel looks, InventoryViewModel inventory, ItemDescriber describer, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(looks);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(describer);

        _looks = looks;
        _inventory = inventory;
        _describer = describer;
        looks.Changed += this.Raise;
        looks.Completed += this.OnCompleted;
        inventory.Changed += this.Raise;
    }

    /// <summary>Gets the player's current look (the server's default until it has been sent).</summary>
    public LookInfo Look => _looks.TryGetLocal(out LookInfo look) ? look : LookInfo.Default;

    /// <summary>
    /// Gets the layers of the character preview (facing down, standing), back to front, with the same frames and offsets the world
    /// sprite uses (<c>FarmerSprite</c>): body, trousers, shirt, arms, hair, hat.
    /// </summary>
    public IReadOnlyList<PreviewLayer> Preview
    {
        get
        {
            const string Farmer = "assets/farmer/";
            LookInfo look = this.Look;
            ClothingSprite shirt = LookPalette.Shirt(look.ShirtItemId);
            ClothingSprite pants = LookPalette.Pants(look.PantsItemId);
            int hair = LookPalette.HairFrame(look.HairStyleId);
            List<PreviewLayer> layers =
            [
                new(Farmer + "body.png", 0, 0, 16, 32, 288, 672, 0, 0, Tint(LookPalette.SkinTint(look.SkinId))),
                new(Farmer + "pants.png", pants.SheetIndex % 10 * 192, pants.SheetIndex / 10 * 672, 16, 32, 1920, 1376, 0, 0, Tint(pants.Tint)),
                new(Farmer + "shirts.png", shirt.SheetIndex % 16 * 8, shirt.SheetIndex / 16 * 32, 8, 8, 256, 608, 4, 15, Tint(shirt.Tint)),
                new(Farmer + "body.png", 6 * 16, 0, 16, 32, 288, 672, 0, 0, null),
                // Boots tier band of boots.png (96 x 384), standing frame facing down (docs/adr/ADR-084).
                new(Farmer + "boots.png", 0, (LookPalette.BootsTier(look.FeetItemId) - 1) * 96, 16, 32, 96, 384, 0, 0, null),
                new(Farmer + "hair.png", hair % 8 * 16, hair / 8 * 96, 16, 32, 128, 672, 0, 2, Tint(LookPalette.DyeTint(look.HairDyeId))),
            ];
            if (LookPalette.TryHat(look.HatItemId, out ClothingSprite hat))
            {
                layers.Add(new(Farmer + "hats.png", hat.SheetIndex % 12 * 20, hat.SheetIndex / 12 * 80, 20, 20, 240, 880, -2, 0, Tint(hat.Tint)));
            }

            return layers;
        }
    }

    /// <summary>Gets the skin tones.</summary>
    public IReadOnlyList<PartChoice> Skins
    {
        get
        {
            List<PartChoice> choices = [];
            for (int id = 1; id <= LookCatalog.SkinCount; id++)
            {
                choices.Add(new PartChoice(id, LookPalette.Css(LookPalette.SkinTint(id)), this.Text("Wardrobe_Skin", id), id == this.Look.SkinId));
            }

            return choices;
        }
    }

    /// <summary>Gets the hair styles.</summary>
    public IReadOnlyList<PartChoice> HairStyles
    {
        get
        {
            List<PartChoice> choices = [];
            for (int id = 1; id <= LookCatalog.HairStyleCount; id++)
            {
                choices.Add(new PartChoice(id, string.Empty, this.Text("Wardrobe_HairStyle", id), id == this.Look.HairStyleId));
            }

            return choices;
        }
    }

    /// <summary>Gets the hair dyes.</summary>
    public IReadOnlyList<PartChoice> HairDyes
    {
        get
        {
            List<PartChoice> choices = [];
            for (int id = 1; id <= LookCatalog.HairDyeCount; id++)
            {
                choices.Add(new PartChoice(id, LookPalette.Css(LookPalette.DyeTint(id)), this.Text("Wardrobe_HairDye", id), id == this.Look.HairDyeId));
            }

            return choices;
        }
    }

    /// <summary>Gets what is worn in each slot.</summary>
    public IReadOnlyList<WornDisplay> Worn
    {
        get
        {
            LookInfo look = this.Look;
            return
            [
                new WornDisplay((int)WearSlot.Shirt, this.Text("Wardrobe_Slot_Shirt"), _describer.Describe((int)WearSlot.Shirt, look.ShirtItemId, look.ShirtItemId == 0 ? 0 : 1)),
                new WornDisplay((int)WearSlot.Pants, this.Text("Wardrobe_Slot_Pants"), _describer.Describe((int)WearSlot.Pants, look.PantsItemId, look.PantsItemId == 0 ? 0 : 1)),
                new WornDisplay((int)WearSlot.Hat, this.Text("Wardrobe_Slot_Hat"), _describer.Describe((int)WearSlot.Hat, look.HatItemId, look.HatItemId == 0 ? 0 : 1)),
                new WornDisplay((int)WearSlot.Feet, this.Text("Wardrobe_Slot_Feet"), _describer.Describe((int)WearSlot.Feet, look.FeetItemId, look.FeetItemId == 0 ? 0 : 1)),
            ];
        }
    }

    /// <summary>Gets the clothing in the inventory; each item's index is its inventory slot.</summary>
    public IReadOnlyList<SlotDisplay> Wardrobe
    {
        get
        {
            List<SlotDisplay> clothes = [];
            foreach (SlotDisplay slot in _inventory.Slots)
            {
                if (!slot.IsEmpty && ItemCatalog.TryGet(slot.ItemId, out ItemDefinition? item) && item.Kind == ItemKind.Clothing)
                {
                    clothes.Add(slot);
                }
            }

            return clothes;
        }
    }

    /// <summary>Gets the localized title of the wardrobe panel.</summary>
    public string Title => this.Text("Wardrobe_Title");

    /// <summary>Gets the localized skin section heading.</summary>
    public string SkinTitle => this.Text("Wardrobe_Skin_Heading");

    /// <summary>Gets the localized hair style section heading.</summary>
    public string HairStyleTitle => this.Text("Wardrobe_HairStyle_Heading");

    /// <summary>Gets the localized hair dye section heading.</summary>
    public string HairDyeTitle => this.Text("Wardrobe_HairDye_Heading");

    /// <summary>Gets the localized worn-items section heading.</summary>
    public string WornTitle => this.Text("Wardrobe_Worn");

    /// <summary>Gets the localized wearable-bag section heading.</summary>
    public string BagTitle => this.Text("Wardrobe_Bag");

    /// <summary>Gets the localized empty-bag text when the player carries no clothing.</summary>
    public string EmptyBagText => this.Text("Wardrobe_BagEmpty");

    /// <summary>Gets the localized text of the take-off button.</summary>
    public string TakeOffText => this.Text("Wardrobe_TakeOff");

    /// <summary>Chooses a skin tone.</summary>
    /// <param name="id">The tone id.</param>
    public void SetSkin(int id) => _looks.SetAppearance(id, this.Look.HairStyleId, this.Look.HairDyeId);

    /// <summary>Chooses a hair style.</summary>
    /// <param name="id">The style id.</param>
    public void SetHairStyle(int id) => _looks.SetAppearance(this.Look.SkinId, id, this.Look.HairDyeId);

    /// <summary>Chooses a hair dye.</summary>
    /// <param name="id">The dye id.</param>
    public void SetHairDye(int id) => _looks.SetAppearance(this.Look.SkinId, this.Look.HairStyleId, id);

    /// <summary>Wears the clothing item in an inventory slot.</summary>
    /// <param name="inventorySlot">The inventory slot.</param>
    public void Wear(int inventorySlot)
    {
        foreach (SlotDisplay slot in this.Wardrobe)
        {
            if (slot.Index == inventorySlot)
            {
                _looks.Equip(inventorySlot, slot.ItemId);
                return;
            }
        }
    }

    /// <summary>Takes the item worn in a slot off.</summary>
    /// <param name="wearSlot">1 shirt, 2 trousers, 3 hat, 4 feet.</param>
    public void TakeOff(int wearSlot)
    {
        if (wearSlot is < (int)WearSlot.Shirt or > (int)WearSlot.Feet)
        {
            return;
        }

        _looks.Unequip(wearSlot);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _looks.Changed -= this.Raise;
        _looks.Completed -= this.OnCompleted;
        _inventory.Changed -= this.Raise;
    }

    // White multiplies to nothing: no layer needed.
    private static string? Tint(int tint) => tint == unchecked((int)0xFFFFFFFF) ? null : LookPalette.Css(tint);

    private void OnCompleted(LookStatus status) => this.SetStatus(status == LookStatus.Ok ? string.Empty : this.Text("Wardrobe_Status_" + status));
}
