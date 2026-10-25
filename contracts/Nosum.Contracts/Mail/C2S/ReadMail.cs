// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Mail.C2S;

/// <summary>Marks a mail read.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ReadMail : PacketBase<ReadMail>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MailOpCode.ReadMail;

    /// <summary>The mail, as listed in the mailbox.</summary>
    [SerializeOrder(0)] public Guid MailId { get; set; }
}
