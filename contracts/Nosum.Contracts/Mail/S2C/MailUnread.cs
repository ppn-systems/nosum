// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Mail.S2C;

/// <summary>Pushed to one player: how many mails are unread now.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MailUnread : PacketBase<MailUnread>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MailOpCode.MailUnread;

    /// <summary>Unread mails.</summary>
    [SerializeOrder(0)] public int Count { get; set; }
}
