// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Emote.S2C;

/// <summary>Pushed to every player who can see the sender: show this emote above the sender's farmer for a few seconds.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EmoteShown : PacketBase<EmoteShown>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EmoteOpCode.EmoteShown;

    /// <summary>The sender's id on the map (the same id as <c>LookRow.PlayerId</c>).</summary>
    [SerializeOrder(0)] public uint PlayerId { get; set; }

    /// <summary>The emote on the wheel, 0 to 11.</summary>
    [SerializeOrder(1)] public byte Emote { get; set; }
}
