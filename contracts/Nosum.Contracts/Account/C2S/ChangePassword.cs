// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Changes the signed-in player's password; every other session of the account ends.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ChangePassword : PacketBase<ChangePassword>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.ChangePassword;

    /// <summary>The current password in plain text. Never logged.</summary>
    [WireMaxLength(WireLimits.Password)]
    [SerializeOrder(0)] public string CurrentPassword { get; set; } = string.Empty;

    /// <summary>The new password in plain text. Never logged.</summary>
    [WireMaxLength(WireLimits.Password)]
    [SerializeOrder(1)] public string NewPassword { get; set; } = string.Empty;
}
