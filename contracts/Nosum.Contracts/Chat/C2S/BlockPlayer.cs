// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Chat.C2S;

/// <summary>Adds a player to the block list: their chat, trade and visit requests are dropped by the server.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class BlockPlayer : PacketBase<BlockPlayer>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ChatOpCode.BlockPlayer;

    /// <summary>The player's nickname.</summary>
    [SerializeOrder(0)] public string Nickname { get; set; } = string.Empty;
}
