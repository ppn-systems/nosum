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
    /// <param name="wearSlot">1 shirt, 2 trousers, 3 hat.</param>
    public void TakeOff(int wearSlot) => _looks.Unequip(wearSlot);

    /// <inheritdoc/>
    public void Dispose()
    {
        _looks.Changed -= this.Raise;
        _looks.Completed -= this.OnCompleted;
        _inventory.Changed -= this.Raise;
    }

    private void OnCompleted(LookStatus status) => this.SetStatus(status == LookStatus.Ok ? string.Empty : this.Text("Wardrobe_Status_" + status));
}
