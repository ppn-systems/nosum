// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Chat.C2S;

/// <summary>Removes a player from the block list.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class UnblockPlayer : PacketBase<UnblockPlayer>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ChatOpCode.UnblockPlayer;

    /// <summary>The player's nickname.</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(0)] public string Nickname { get; set; } = string.Empty;
}
