// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>Reply to <see cref="C2S.GetPlaced"/>: every placed object of the player's current place.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PlacedSnapshot : PacketBase<PlacedSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.PlacedSnapshot;

    /// <summary>The map the objects stand on; empty when the player is on no placeable map.</summary>
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;

    /// <summary>The objects.</summary>
    [SerializeOrder(1)] public List<PlacedObjectRow> Objects { get; set; } = [];
}
