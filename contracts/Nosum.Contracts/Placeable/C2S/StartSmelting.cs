// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Placeable.C2S;

/// <summary>Puts ore and coal into a furnace: five of the ore and one coal leave the inventory and a 30 game-minute job starts (docs/adr/ADR-024 D4). Reply: <see cref="S2C.FurnaceResult"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class StartSmelting : PacketBase<StartSmelting>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.StartSmelting;

    /// <summary>Id of the placed furnace.</summary>
    [WireRange(0, int.MaxValue)]
    [SerializeOrder(0)] public int ObjectId { get; set; }

    /// <summary>The ore to smelt: copper ore or iron ore.</summary>
    [WireRange(0, WireLimits.MaxId)]
    [SerializeOrder(1)] public int OreItemId { get; set; }
}
