// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Player.C2S;

/// <summary>Asks the server to put the player on a map. The server decides where they stand.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class JoinMap : PacketBase<JoinMap>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlayerOpCode.JoinMap;

    /// <summary>Id of the map to enter, for example <c>farm</c>.</summary>
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;
}
