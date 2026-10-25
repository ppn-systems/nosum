// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Building;

/// <summary>
/// Opcodes of farm buildings and animals (docs/adr/ADR-028 D6).
/// <para>Slice <c>0x3100-0x31FF</c> of the Farm band is reserved for this domain. C2S requests use <c>0x3100-0x313F</c>, S2C replies and pushes use <c>0x3140-0x317F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class BuildingOpCode
{
    // -- C2S (0x3100-0x313F) --------------------------------------------------

    /// <summary>Asks for the buildings of the land the player stands on, or of their own lands.</summary>
    public const ushort GetBuildings = 0x3100;

    /// <summary>Asks the carpenter for a building's blueprint.</summary>
    public const ushort CommissionBuilding = 0x3101;

    /// <summary>Buys an animal at the carpenter.</summary>
    public const ushort BuyAnimal = 0x3102;

    /// <summary>Asks for the animals of the building the player stands in.</summary>
    public const ushort GetAnimals = 0x3103;

    /// <summary>Puts feed into the feeding bench.</summary>
    public const ushort FeedAnimals = 0x3104;

    /// <summary>Pets an animal.</summary>
    public const ushort PetAnimal = 0x3105;

    /// <summary>Collects an animal's produce.</summary>
    public const ushort CollectProduce = 0x3106;

    // -- S2C (0x3140-0x317F) --------------------------------------------------

    /// <summary>The outcome of a commission, a purchase or a feeding.</summary>
    public const ushort BuildingResult = 0x3140;

    /// <summary>The buildings of a land.</summary>
    public const ushort BuildingsSnapshot = 0x3141;

    /// <summary>The animals of a building.</summary>
    public const ushort AnimalsSnapshot = 0x3142;

    /// <summary>A building's animals or feeding bench changed.</summary>
    public const ushort AnimalsChanged = 0x3143;

    /// <summary>New walks of a building's animals.</summary>
    public const ushort AnimalsMoved = 0x3144;

    /// <summary>The outcome of petting or collecting.</summary>
    public const ushort AnimalResult = 0x3145;
}
