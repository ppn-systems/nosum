// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Node.S2C;

/// <summary>Reply to <see cref="C2S.GetNodes"/>: every node of the place, and the state of the player's watering can.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class NodeSnapshot : PacketBase<NodeSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => NodeOpCode.NodeSnapshot;

    /// <summary>The map the nodes stand on; empty when the player is on no map.</summary>
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;

    /// <summary>Water left in the player's watering can.</summary>
    [SerializeOrder(1)] public byte WaterLeft { get; set; }

    /// <summary>Largest amount the can holds.</summary>
    [SerializeOrder(2)] public byte WaterCapacity { get; set; }

    /// <summary>Every node of the place.</summary>
    [SerializeOrder(3)] public List<NodeRow> Nodes { get; set; } = [];
}
