// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Combat.S2C;

/// <summary>Reply to <see cref="C2S.DrinkPotion"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DrinkPotionResult : PacketBase<DrinkPotionResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.DrinkPotionResult;

    /// <summary>Outcome of drinking the potion.</summary>
    [SerializeOrder(0)] public CombatStatus Status { get; set; }

    /// <summary>Updated player HP.</summary>
    [SerializeOrder(1)] public ushort Hp { get; set; }

    /// <summary>Updated player MaxHp.</summary>
    [SerializeOrder(2)] public ushort MaxHp { get; set; }

    /// <summary>Updated player MP.</summary>
    [SerializeOrder(3)] public ushort Mp { get; set; }

    /// <summary>Updated player MaxMp.</summary>
    [SerializeOrder(4)] public ushort MaxMp { get; set; }
}
