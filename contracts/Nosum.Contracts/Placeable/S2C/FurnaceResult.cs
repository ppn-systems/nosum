// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>Reply to <see cref="C2S.StartSmelting"/> and <see cref="C2S.CollectFurnace"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class FurnaceResult : PacketBase<FurnaceResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.FurnaceResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public FurnaceStatus Status { get; set; }

    /// <summary>Id of the furnace.</summary>
    [SerializeOrder(1)] public int ObjectId { get; set; }

    /// <summary>The bar collected; zero otherwise.</summary>
    [SerializeOrder(2)] public int ItemId { get; set; }

    /// <summary>How many bars were collected; zero otherwise.</summary>
    [SerializeOrder(3)] public int Quantity { get; set; }
}
