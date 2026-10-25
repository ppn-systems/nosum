// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Placeable.S2C;

namespace Nosum.Contracts.World.S2C;

/// <summary>
/// Push to one player: the full state of one chunk (16 × 16 tiles) that just joined the player's interest set (docs/adr/ADR-022 D4).
/// It replaces whatever the client mirrors of that chunk; changes in it follow as the usual delta packets until
/// <see cref="ChunkLeft"/>. A chunk with nothing in it is not sent at all. Lists of other world things (resource nodes, ground items)
/// are appended here as their features land.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ChunkState : PacketBase<ChunkState>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => WorldOpCode.ChunkState;

    /// <summary>The map the chunk belongs to.</summary>
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;

    /// <summary>Chunk column.</summary>
    [SerializeOrder(1)] public ushort ChunkX { get; set; }

    /// <summary>Chunk row.</summary>
    [SerializeOrder(2)] public ushort ChunkY { get; set; }

    /// <summary>The server's wall clock when the state was read, Unix milliseconds; farm times are on this clock.</summary>
    [SerializeOrder(3)] public long ServerNowMs { get; set; }

    /// <summary>Every tilled tile in the chunk.</summary>
    [SerializeOrder(4)] public List<FarmTileRow> FarmTiles { get; set; } = [];

    /// <summary>Every placed object whose first tile is in the chunk.</summary>
    [SerializeOrder(5)] public List<PlacedObjectRow> Placed { get; set; } = [];

    /// <summary>Gets whether the chunk holds nothing the packet can carry; such a chunk is not sent.</summary>
    [SerializeIgnore] public bool IsEmpty => this.FarmTiles.Count == 0 && this.Placed.Count == 0;
}
