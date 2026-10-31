// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.Game;

/// <summary>
/// What a farmer wears: style indices into the layer sheets and tint colours (docs/adr/ADR-006).
/// </summary>
/// <param name="Hairstyle">Index into <c>hair.png</c> (8 styles per row).</param>
/// <param name="HairTint">Hair colour, <c>0xRRGGBBAA</c>.</param>
/// <param name="Shirt">Index into <c>shirts.png</c> (16 shirts per row).</param>
/// <param name="Pants">Index into <c>pants.png</c> (10 styles per row).</param>
/// <param name="PantsTint">Pants colour, <c>0xRRGGBBAA</c>.</param>
/// <param name="SkinTint">Body colour, <c>0xRRGGBBAA</c>.</param>
/// <param name="Hat">Index into <c>hats.png</c> (12 hats per row), or -1 for no hat.</param>
/// <param name="Boots">Boots tier 1 to 4 (boots.png band), or 0 for bare feet (docs/adr/ADR-082).</param>
/// <param name="HatTint">Hat colour, <c>0xRRGGBBAA</c>.</param>
/// <param name="ShirtTint">Shirt colour, <c>0xRRGGBBAA</c>.</param>
internal readonly record struct FarmerLook(int Hairstyle, int HairTint, int Shirt, int Pants, int PantsTint, int SkinTint = unchecked((int)0xFFFFFFFF), int Hat = -1, int HatTint = unchecked((int)0xFFFFFFFF), int ShirtTint = unchecked((int)0xFFFFFFFF), int Boots = 0);
