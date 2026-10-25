// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Building.S2C;

/// <summary>The outcome of petting an animal or collecting its produce.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AnimalResult : PacketBase<AnimalResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => BuildingOpCode.AnimalResult;

    /// <summary>How it went.</summary>
    [SerializeOrder(0)] public BuildingStatus Status { get; set; }

    /// <summary>The produce item; zero when none.</summary>
    [SerializeOrder(1)] public int ItemId { get; set; }

    /// <summary>How many.</summary>
    [SerializeOrder(2)] public int Quantity { get; set; }

    /// <summary>The animal's friendship now.</summary>
    [SerializeOrder(3)] public int Friendship { get; set; }
}
