// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Building.S2C;

/// <summary>The animals of the building the player stands in, with the feeding bench and who may tend them.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AnimalsSnapshot : PacketBase<AnimalsSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => BuildingOpCode.AnimalsSnapshot;

    /// <summary>The building.</summary>
    [SerializeOrder(0)] public Guid BuildingId { get; set; }

    /// <summary>The building kind.</summary>
    [SerializeOrder(1)] public byte Kind { get; set; }

    /// <summary>Rations in the feeding bench.</summary>
    [SerializeOrder(2)] public int Rations { get; set; }

    /// <summary>Whether the player may feed, pet and collect here; a visitor may only look.</summary>
    [SerializeOrder(3)] public bool MayTend { get; set; }

    /// <summary>The animals.</summary>
    [SerializeOrder(4)] public List<AnimalRow> Animals { get; set; } = [];
}
