// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Asks the server to e-mail a one-time recovery code to a registered account.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RequestPasswordReset : PacketBase<RequestPasswordReset>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.RequestPasswordReset;

    /// <summary>The account e-mail (case-insensitive).</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(0)] public string Email { get; set; } = string.Empty;
}
