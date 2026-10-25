// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Building.C2S;

/// <summary>Puts hay or fiber from an inventory slot into the feeding bench of the building the player stands in.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class FeedAnimals : PacketBase<FeedAnimals>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => BuildingOpCode.FeedAnimals;

    /// <summary>Inventory slot holding the feed.</summary>
    [SerializeOrder(0)] public int Slot { get; set; }

    /// <summary>How many to put in.</summary>
    [SerializeOrder(1)] public int Quantity { get; set; }
}
