// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Emote.C2S;

/// <summary>Shows an emote above the sender. Only the code travels: the client draws the icon (docs/adr/ADR-071 D1).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SendEmote : PacketBase<SendEmote>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EmoteOpCode.SendEmote;

    /// <summary>The emote on the wheel, 0 to 11.</summary>
    [SerializeOrder(0)] public byte Emote { get; set; }
}
