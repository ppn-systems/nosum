// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Farm.C2S;

/// <summary>
/// Uses the item in a hotbar slot on a tile, for example "hoe on tile (12, 9)". The server decides what
/// that does from the item and the tile; a mature crop is harvested whatever is held. The server checks
/// the item, the tile and the distance to the player (an intent, never a result).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class UseItem : PacketBase<UseItem>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FarmOpCode.UseItem;

    /// <summary>Hotbar slot, zero to eight.</summary>
    [SerializeOrder(0)] public byte Slot { get; set; }

    /// <summary>Target tile x on the player's map.</summary>
    [SerializeOrder(1)] public int TileX { get; set; }

    /// <summary>Target tile y on the player's map.</summary>
    [SerializeOrder(2)] public int TileY { get; set; }
}
