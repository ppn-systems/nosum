// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.S2C;

/// <summary>The server's reply to <see cref="C2S.RequestPasswordReset"/>, <see cref="C2S.ConfirmPasswordReset"/> and <see cref="C2S.ChangePassword"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PasswordResult : PacketBase<PasswordResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.PasswordResult;

    /// <summary>Outcome. A recovery request answers Ok whether or not the address has an account.</summary>
    [SerializeOrder(0)] public AccountStatus Status { get; set; }
}
