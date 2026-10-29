// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.S2C;

/// <summary>Retired: the reply to the password change. Retired (docs/adr/ADR-069): the class and its opcode stay reserved and are never reused; it has no fields and the server has no handler.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PasswordResult : PacketBase<PasswordResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.PasswordResult;
}
