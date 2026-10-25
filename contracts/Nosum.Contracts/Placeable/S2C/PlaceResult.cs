// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>Reply to <see cref="C2S.PlaceObject"/>, <see cref="C2S.MoveObject"/> and <see cref="C2S.PickUpObject"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PlaceResult : PacketBase<PlaceResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.PlaceResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public PlaceStatus Status { get; set; }

    /// <summary>Id of the object that was placed, moved or picked up; zero on a refusal.</summary>
    [SerializeOrder(1)] public int ObjectId { get; set; }
}
