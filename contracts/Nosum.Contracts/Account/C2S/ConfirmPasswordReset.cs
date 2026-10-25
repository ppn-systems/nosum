// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Sets a new password with the one-time code received by e-mail.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ConfirmPasswordReset : PacketBase<ConfirmPasswordReset>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.ConfirmPasswordReset;

    /// <summary>The account e-mail.</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(0)] public string Email { get; set; } = string.Empty;

    /// <summary>The 8-digit code from the e-mail. Never logged.</summary>
    [WireMaxLength(WireLimits.RecoveryCode)]
    [SerializeOrder(1)] public string Code { get; set; } = string.Empty;

    /// <summary>The new password in plain text; the transport protects the wire. Never logged.</summary>
    [WireMaxLength(WireLimits.Password)]
    [SerializeOrder(2)] public string NewPassword { get; set; } = string.Empty;
}
