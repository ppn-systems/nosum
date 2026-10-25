// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.Game;

/// <summary>
/// What a farmer wears: style indices into the layer sheets and tint colours (docs/adr/ADR-006).
/// </summary>
/// <param name="Hairstyle">Index into <c>hairstyles.png</c> (8 styles per row).</param>
/// <param name="HairTint">Hair colour, <c>0xRRGGBBAA</c>.</param>
/// <param name="Shirt">Index into <c>shirts.png</c> (16 shirts per row).</param>
/// <param name="Pants">Index into <c>pants.png</c> (10 styles per row).</param>
/// <param name="PantsTint">Pants colour, <c>0xRRGGBBAA</c>.</param>
internal readonly record struct FarmerLook(int Hairstyle, int HairTint, int Shirt, int Pants, int PantsTint);
