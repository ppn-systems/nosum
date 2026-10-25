// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.GroundItem.C2S;

/// <summary>Asks for every ground item of the instance the player is in. Send it after joining a map; identity comes from the connection.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetGroundItems : PacketBase<GetGroundItems>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => GroundItemOpCode.GetGroundItems;
}
