// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Player.C2S;

/// <summary>
/// Asks the server to put the player in the world. The server decides the map and where they stand: the map
/// they were last on, else the starting map. <see cref="MapId"/> only has to name a map the server hosts.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class JoinMap : PacketBase<JoinMap>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlayerOpCode.JoinMap;

    /// <summary>Id of a hosted map, for example <c>farm</c>; an unknown id is refused with <see cref="JoinStatus.UnknownMap"/>. It does not choose the map.</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;
}
