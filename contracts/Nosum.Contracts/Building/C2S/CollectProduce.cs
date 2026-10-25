// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Building.C2S;

/// <summary>Collects an animal's produce for the day; a cow needs the milk pail in the named slot.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CollectProduce : PacketBase<CollectProduce>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => BuildingOpCode.CollectProduce;

    /// <summary>The animal.</summary>
    [SerializeOrder(0)] public Guid AnimalId { get; set; }

    /// <summary>The inventory slot of the tool in hand.</summary>
    [SerializeOrder(1)] public int Slot { get; set; }
}
