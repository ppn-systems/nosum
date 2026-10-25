// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Inventory;

/// <summary>What a slot control shows: already localized, so the control holds no text of its own.</summary>
/// <param name="Index">Slot number, zero based.</param>
/// <param name="ItemId">Item id; zero for an empty slot.</param>
/// <param name="Quantity">How many are stacked.</param>
/// <param name="Name">Localized item name; empty for an empty slot.</param>
/// <param name="Tooltip">Localized tooltip (name, description, quantity); empty for an empty slot.</param>
/// <param name="Icon">The item's picture, or <see langword="null"/>.</param>
/// <param name="IsSelected">Whether this is the held hotbar slot.</param>
public sealed record SlotDisplay(int Index, int ItemId, int Quantity, string Name, string Tooltip, ItemIcon? Icon, bool IsSelected)
{
    /// <summary>Gets whether the slot holds nothing.</summary>
    public bool IsEmpty => this.ItemId == 0;
}
