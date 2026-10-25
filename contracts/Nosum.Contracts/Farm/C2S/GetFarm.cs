// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Farm.C2S;

/// <summary>Asks for the worked tiles of the map the player stands on. Send it after joining a map.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetFarm : PacketBase<GetFarm>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FarmOpCode.GetFarm;

    /// <summary>Reserved; the request carries no data.</summary>
    [SerializeOrder(0)] public byte Reserved { get; set; }
}
