// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Mail.C2S;

/// <summary>Asks for the player's mailbox.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetMail : PacketBase<GetMail>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MailOpCode.GetMail;
}
