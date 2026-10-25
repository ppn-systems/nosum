// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Building.C2S;

/// <summary>Asks for the animals of the building the player stands in.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetAnimals : PacketBase<GetAnimals>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => BuildingOpCode.GetAnimals;
}
