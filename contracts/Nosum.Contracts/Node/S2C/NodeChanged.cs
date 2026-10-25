// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Node.S2C;

/// <summary>Pushed to the players of an instance after nodes changed: each added or changed node in its new state, and the ids of removed ones.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class NodeChanged : PacketBase<NodeChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => NodeOpCode.NodeChanged;

    /// <summary>The map the nodes stand on.</summary>
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;

    /// <summary>Nodes that were added or changed (a tree becoming a stump keeps its id).</summary>
    [SerializeOrder(1)] public List<NodeRow> Upserted { get; set; } = [];

    /// <summary>Ids of nodes that were removed.</summary>
    [SerializeOrder(2)] public List<int> Removed { get; set; } = [];
}
