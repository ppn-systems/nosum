// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Mail.S2C;

/// <summary>The receiver's mailbox, newest first.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MailList : PacketBase<MailList>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MailOpCode.MailList;

    /// <summary>How many mails are unread.</summary>
    [SerializeOrder(0)] public int Unread { get; set; }

    /// <summary>The mails.</summary>
    [SerializeOrder(1)] public List<MailRow> Mails { get; set; } = [];
}
