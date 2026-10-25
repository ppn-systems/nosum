// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Building.S2C;

/// <summary>The buildings of a land, as a reply or pushed to everybody there when one changes.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class BuildingsSnapshot : PacketBase<BuildingsSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => BuildingOpCode.BuildingsSnapshot;

    /// <summary>Echoes the request's scope.</summary>
    [SerializeOrder(0)] public bool Mine { get; set; }

    /// <summary>The buildings.</summary>
    [SerializeOrder(1)] public List<BuildingRow> Buildings { get; set; } = [];
}
