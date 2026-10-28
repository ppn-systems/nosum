// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Combat.C2S;

/// <summary>Drinks a potion in <see cref="Slot"/> to restore HP and MP. Reply: <see cref="S2C.DrinkPotionResult"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DrinkPotion : PacketBase<DrinkPotion>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.DrinkPotion;

    /// <summary>Hotbar/inventory slot holding the potion.</summary>
    [SerializeOrder(0)] public byte Slot { get; set; }
}
