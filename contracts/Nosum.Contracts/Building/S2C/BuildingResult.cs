// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Building.S2C;

/// <summary>The outcome of a commission, a purchase or a feeding.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class BuildingResult : PacketBase<BuildingResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => BuildingOpCode.BuildingResult;

    /// <summary>How it went.</summary>
    [SerializeOrder(0)] public BuildingStatus Status { get; set; }

    /// <summary>The new animal's id for a purchase; empty otherwise.</summary>
    [SerializeOrder(1)] public Guid Id { get; set; }

    /// <summary>The item handed over (the blueprint) or put in (the feed); zero otherwise.</summary>
    [SerializeOrder(2)] public int ItemId { get; set; }

    /// <summary>How many; for a feeding, the rations now in the bench.</summary>
    [SerializeOrder(3)] public int Quantity { get; set; }
}
