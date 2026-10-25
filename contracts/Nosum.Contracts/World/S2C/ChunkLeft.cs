// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.World.S2C;

/// <summary>
/// Push to one player: a chunk left the player's interest set (docs/adr/ADR-022 D6). The client drops its mirror of the chunk's world state
/// and stops expecting changes from it.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ChunkLeft : PacketBase<ChunkLeft>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => WorldOpCode.ChunkLeft;

    /// <summary>The map the chunk belongs to.</summary>
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;

    /// <summary>Chunk column.</summary>
    [SerializeOrder(1)] public ushort ChunkX { get; set; }

    /// <summary>Chunk row.</summary>
    [SerializeOrder(2)] public ushort ChunkY { get; set; }
}
