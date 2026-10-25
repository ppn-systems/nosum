// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Node.S2C;

/// <summary>Pushed to the players of an instance after a pickaxe cleared tilled soil: those tiles are no longer worked.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class FarmTilesCleared : PacketBase<FarmTilesCleared>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => NodeOpCode.FarmTilesCleared;

    /// <summary>The map the tiles belong to.</summary>
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;

    /// <summary>The cleared tiles.</summary>
    [SerializeOrder(1)] public List<TileRow> Tiles { get; set; } = [];
}
