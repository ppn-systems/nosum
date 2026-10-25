// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.Game;

/// <summary>
/// A stand-in look for farmers other than the local one, derived from the player id so the same
/// player always looks the same. The wire carries no look yet (docs/adr/ADR-006 customization
/// sync is a later change).
/// </summary>
internal static class RemoteLook
{
    private static readonly int[] s_hair = [unchecked((int)0x6B4226FF), unchecked((int)0xD9B25CFF), unchecked((int)0x2B2B2BFF), unchecked((int)0xB5472EFF), unchecked((int)0x8F8F8FFF)];
    private static readonly int[] s_pants = [unchecked((int)0x3E5F8AFF), unchecked((int)0x5A4632FF), unchecked((int)0x4C7A4CFF), unchecked((int)0x7A3E3EFF)];

    /// <summary>Returns the look of a player.</summary>
    /// <param name="playerId">Player id.</param>
    /// <returns>What the farmer wears.</returns>
    public static FarmerLook For(uint playerId)
    {
        int n = (int)(playerId % 1000);
        return new FarmerLook(
            Hairstyle: (n * 3) % 8,
            HairTint: s_hair[n % s_hair.Length],
            Shirt: ((n * 5) + 2) % 16,
            Pants: n % 10,
            PantsTint: s_pants[(n / 2) % s_pants.Length]);
    }
}
