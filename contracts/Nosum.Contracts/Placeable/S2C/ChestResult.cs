// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>Reply to <see cref="C2S.ChestMove"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ChestResult : PacketBase<ChestResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.ChestResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public ChestStatus Status { get; set; }

    /// <summary>Id of the chest.</summary>
    [SerializeOrder(1)] public int ObjectId { get; set; }
}
