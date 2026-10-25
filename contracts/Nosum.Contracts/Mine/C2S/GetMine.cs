// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Mine.C2S;

/// <summary>Asks for the player's mine progress. Reply: <see cref="S2C.MineState"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetMine : PacketBase<GetMine>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MineOpCode.GetMine;
}
