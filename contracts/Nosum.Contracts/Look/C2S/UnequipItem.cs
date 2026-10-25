// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Look.C2S;

/// <summary>Takes the item worn in a slot off (1 shirt, 2 trousers, 3 hat).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class UnequipItem : PacketBase<UnequipItem>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => LookOpCode.UnequipItem;

    /// <summary>Where; the values of the gameplay wear slot.</summary>
    [SerializeOrder(0)] public byte WearSlot { get; set; }
}
