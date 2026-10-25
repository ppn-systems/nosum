// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Player.C2S;

/// <summary>Eats the food in a hotbar slot: the server removes one and restores its energy (docs/adr/ADR-023 D2). Reply: <see cref="S2C.EatResult"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class Eat : PacketBase<Eat>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlayerOpCode.Eat;

    /// <summary>Hotbar slot, zero to eight.</summary>
    [SerializeOrder(0)] public byte Slot { get; set; }
}
