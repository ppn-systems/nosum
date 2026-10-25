// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Player.S2C;

/// <summary>Pushed to the rest of a map when a player leaves it or disconnects.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PlayerLeft : PacketBase<PlayerLeft>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlayerOpCode.PlayerLeft;

    /// <summary>Id of the player who left.</summary>
    [SerializeOrder(0)] public uint PlayerId { get; set; }
}
