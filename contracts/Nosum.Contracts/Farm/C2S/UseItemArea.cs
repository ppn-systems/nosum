// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Farm.C2S;

/// <summary>
/// Uses the item in a hotbar slot on a line of tiles (docs/adr/ADR-026 D1): the first tile is the one in front of the farmer, the others follow it the
/// way the server knows the farmer faces. Only a hoe or a watering can works a line, and only as long as its tier allows (1, 3 or 5 tiles); the server
/// checks the claim against the item in the slot and refuses the rest. Reply: <see cref="S2C.UseItemResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class UseItemArea : PacketBase<UseItemArea>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FarmOpCode.UseItemArea;

    /// <summary>Hotbar slot, zero to eight.</summary>
    [SerializeOrder(0)] public byte Slot { get; set; }

    /// <summary>First tile x, on the player's map.</summary>
    [SerializeOrder(1)] public int TileX { get; set; }

    /// <summary>First tile y, on the player's map.</summary>
    [SerializeOrder(2)] public int TileY { get; set; }

    /// <summary>How many tiles in a line: 1, 3 or 5.</summary>
    [SerializeOrder(3)] public byte Tiles { get; set; }
}
