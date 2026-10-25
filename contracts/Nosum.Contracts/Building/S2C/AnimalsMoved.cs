// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Building.S2C;

/// <summary>Pushed to everybody inside a building every few seconds: the new straight walks the server chose for its animals.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AnimalsMoved : PacketBase<AnimalsMoved>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => BuildingOpCode.AnimalsMoved;

    /// <summary>The building.</summary>
    [SerializeOrder(0)] public Guid BuildingId { get; set; }

    /// <summary>The walks.</summary>
    [SerializeOrder(1)] public List<AnimalMoveRow> Moves { get; set; } = [];
}
