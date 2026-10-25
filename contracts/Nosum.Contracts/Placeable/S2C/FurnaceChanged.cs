// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>Pushed to every player of an instance after a furnace changed state: a job started, finished or was collected.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class FurnaceChanged : PacketBase<FurnaceChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.FurnaceChanged;

    /// <summary>Id of the furnace.</summary>
    [SerializeOrder(0)] public int ObjectId { get; set; }

    /// <summary>Where the furnace stands now.</summary>
    [SerializeOrder(1)] public FurnaceStateCode State { get; set; }

    /// <summary>The ore being smelted; zero when idle.</summary>
    [SerializeOrder(2)] public int OreItemId { get; set; }

    /// <summary>Milliseconds until the bar is ready, counted from when this packet was sent; zero when ready or idle. The client counts down locally.</summary>
    [SerializeOrder(3)] public int RemainingMs { get; set; }
}
