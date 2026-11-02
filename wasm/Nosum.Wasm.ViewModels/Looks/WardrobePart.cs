// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Looks;

/// <summary>One part of the wardrobe window; the window shows one part at a time, chosen on its tab bar (docs/design/ui-standard.md section 3).</summary>
public enum WardrobePart
{
    /// <summary>The look: skin tone, hair style and hair colour.</summary>
    Look,

    /// <summary>The clothes: what the character wears, and the clothes in the bag.</summary>
    Clothes,
}
