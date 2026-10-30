// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Fishing;

namespace Nosum.Wasm.ViewModels.Fishing;

/// <summary>
/// The fish the server reeled in, for the catch card (docs/adr/ADR-078 polish). The server rolled the fish, its size and the item; the client only
/// names it and picks the rarity colour.
/// </summary>
/// <param name="ItemId">The item id of the fish, which is also its icon.</param>
/// <param name="Name">The fish's name in the player's language.</param>
/// <param name="SizeCm">The fish's length in centimetres, as the server rolled it.</param>
/// <param name="Rarity">How rare the fish is, for the colour of the card.</param>
public sealed record FishCatch(int ItemId, string Name, int SizeCm, FishRarity Rarity);
