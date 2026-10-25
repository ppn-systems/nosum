// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Chat.C2S;

/// <summary>Sends a chat message. The text is player-authored: the server cleans it (NFC, no control characters, at most 160 characters) and masks profanity.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SendChat : PacketBase<SendChat>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ChatOpCode.SendChat;

    /// <summary>The channel to send to.</summary>
    [SerializeOrder(0)] public ChatChannel Channel { get; set; }

    /// <summary>The addressee's nickname for a whisper; empty for the other channels.</summary>
    [SerializeOrder(1)] public string Target { get; set; } = string.Empty;

    /// <summary>The text as typed.</summary>
    [SerializeOrder(2)] public string Text { get; set; } = string.Empty;
}
