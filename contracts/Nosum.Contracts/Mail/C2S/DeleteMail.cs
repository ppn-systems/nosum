// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Mail.C2S;

/// <summary>Deletes a mail that carries no uncollected attachment.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DeleteMail : PacketBase<DeleteMail>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MailOpCode.DeleteMail;

    /// <summary>The mail, as listed in the mailbox.</summary>
    [SerializeOrder(0)] public Guid MailId { get; set; }
}
