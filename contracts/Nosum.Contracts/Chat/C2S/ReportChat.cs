// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Chat.C2S;

/// <summary>Reports a player for what they wrote; the server stores the last messages the reporter received as context for the admins.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ReportChat : PacketBase<ReportChat>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ChatOpCode.ReportChat;

    /// <summary>The player's nickname.</summary>
    [SerializeOrder(0)] public string Nickname { get; set; } = string.Empty;
}
