// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Asks the server to end the session: its token stops working and the connection is signed out.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class Logout : PacketBase<Logout>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.Logout;

    /// <summary>Reserved; the request carries no data.</summary>
    [SerializeOrder(0)] public byte Reserved { get; set; }
}
