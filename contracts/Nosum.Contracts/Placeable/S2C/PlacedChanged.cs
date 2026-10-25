// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>Pushed to every player of an instance after objects changed: each added or changed object in its new state, and the ids of removed ones.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PlacedChanged : PacketBase<PlacedChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.PlacedChanged;

    /// <summary>The map the objects stand on.</summary>
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;

    /// <summary>Objects that were added or changed.</summary>
    [SerializeOrder(1)] public List<PlacedObjectRow> Upserted { get; set; } = [];

    /// <summary>Ids of objects that were removed.</summary>
    [SerializeOrder(2)] public List<int> Removed { get; set; } = [];
}
