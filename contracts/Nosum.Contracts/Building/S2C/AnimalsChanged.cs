// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Building.S2C;

/// <summary>Pushed to everybody inside a building when an animal arrives, changes or goes, or the feeding bench changes.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AnimalsChanged : PacketBase<AnimalsChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => BuildingOpCode.AnimalsChanged;

    /// <summary>The building.</summary>
    [SerializeOrder(0)] public Guid BuildingId { get; set; }

    /// <summary>Rations in the feeding bench.</summary>
    [SerializeOrder(1)] public int Rations { get; set; }

    /// <summary>Animals that are new or changed.</summary>
    [SerializeOrder(2)] public List<AnimalRow> Upserted { get; set; } = [];

    /// <summary>Animals that left.</summary>
    [SerializeOrder(3)] public List<Guid> Removed { get; set; } = [];
}
