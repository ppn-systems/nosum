// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.S2C;

/// <summary>
/// Pushed to a connection just before the server closes it because the player signed in elsewhere.
/// The client shows localized text for the code and returns to the account form.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SessionEnded : PacketBase<SessionEnded>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.SessionEnded;

    /// <summary>Why the session ended; <see cref="AccountStatus.SessionReplaced"/> today.</summary>
    [SerializeOrder(0)] public AccountStatus Reason { get; set; }
}
