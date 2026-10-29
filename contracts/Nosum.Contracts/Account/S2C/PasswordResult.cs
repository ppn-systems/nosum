// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.S2C;

/// <summary>The server's reply to <see cref="C2S.ChangePassword"/>. The replies to the retired recovery packets are gone with them.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PasswordResult : PacketBase<PasswordResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.PasswordResult;

    /// <summary>Outcome of the password change.</summary>
    [SerializeOrder(0)] public AccountStatus Status { get; set; }
}
