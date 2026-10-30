// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Emote.S2C;

/// <summary>Reply to <c>SendEmote</c>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EmoteResult : PacketBase<EmoteResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EmoteOpCode.EmoteResult;

    /// <summary>The outcome.</summary>
    [SerializeOrder(0)] public EmoteStatus Status { get; set; }
}
