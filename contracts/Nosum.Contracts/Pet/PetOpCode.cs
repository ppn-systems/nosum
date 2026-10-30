// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Pet;

/// <summary>
/// Opcodes of the Pet domain (docs/adr/ADR-075 D3-D5). Sub-range <c>0x9500-0x95FF</c> of the Trade band (docs/adr/ADR-029 D6). C2S requests use
/// <c>0x9500-0x953F</c>, the S2C reply <c>0x9540-0x957F</c>. Never reassign or reuse a value. A pet's kind and name reach the other players
/// in <c>LookRow</c>; only the owner reads the mood here.
/// </summary>
public static class PetOpCode
{
    // -- C2S --

    /// <summary>Asks for the player's pet. Reply: <see cref="S2C.PetState"/>.</summary>
    public const ushort GetPet = 0x9500;

    /// <summary>Adopts a cat or a dog and names it. Reply: <see cref="S2C.PetState"/>.</summary>
    public const ushort AdoptPet = 0x9501;

    /// <summary>Pets the player's pet, once a game day. Reply: <see cref="S2C.PetState"/>.</summary>
    public const ushort PetPet = 0x9502;

    // -- S2C --

    /// <summary>Reply to every pet request: the outcome and the pet as it stands.</summary>
    public const ushort PetState = 0x9540;
}
