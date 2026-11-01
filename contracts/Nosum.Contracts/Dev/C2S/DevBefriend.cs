// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Dev.C2S;

/// <summary>
/// Development only: makes the sender a registered test account (if it is a guest) and befriends a new registered test player, whose nickname
/// and session token come back in the reply so a second client can sign in as that player (docs/adr/ADR-100). Not handled in Production.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevBefriend : PacketBase<DevBefriend>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevBefriend;
}
