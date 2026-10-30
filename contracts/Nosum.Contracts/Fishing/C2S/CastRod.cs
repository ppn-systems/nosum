// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Fishing.C2S;

/// <summary>
/// Casts the held fishing rod at a tile. The server checks the rod, the water, the reach and the energy, starts the cast on its own clock and
/// answers with the bite delay. Reply: <see cref="S2C.CastResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CastRod : PacketBase<CastRod>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FishingOpCode.CastRod;

    /// <summary>Tile x of the water the line goes into.</summary>
    [WireRange(WireLimits.TileMin, WireLimits.TileMax)]
    [SerializeOrder(0)] public int TileX { get; set; }

    /// <summary>Tile y of the water the line goes into.</summary>
    [WireRange(WireLimits.TileMin, WireLimits.TileMax)]
    [SerializeOrder(1)] public int TileY { get; set; }
}
