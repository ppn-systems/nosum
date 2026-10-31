// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Dev.C2S;

/// <summary>Development only: moves the sender to a tile of a map. The server refuses a map it does not know, a private map of somebody else, and a tile outside the map. Not handled in Production.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevTeleport : PacketBase<DevTeleport>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevTeleport;

    /// <summary>The map id, as the map catalogue names it.</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;

    /// <summary>The tile column to arrive on.</summary>
    [WireRange(WireLimits.TileMin, WireLimits.TileMax)]
    [SerializeOrder(1)] public int TileX { get; set; }

    /// <summary>The tile row to arrive on.</summary>
    [WireRange(WireLimits.TileMin, WireLimits.TileMax)]
    [SerializeOrder(2)] public int TileY { get; set; }
}
