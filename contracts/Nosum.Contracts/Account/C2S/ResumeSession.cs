// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Asks the server to sign the connection in again with a previously issued session token.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ResumeSession : PacketBase<ResumeSession>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.ResumeSession;

    /// <summary>The token from <see cref="S2C.AccountResult.SessionToken"/>. Never logged.</summary>
    [SerializeOrder(0)] public string SessionToken { get; set; } = string.Empty;
}
