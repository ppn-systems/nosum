// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Chat.S2C;

/// <summary>Pushed to one player: a chat message. The text is player-authored and shown verbatim as plain text (docs/adr/ADR-029 D2).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ChatMessage : PacketBase<ChatMessage>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ChatOpCode.ChatMessage;

    /// <summary>The channel the message was sent on.</summary>
    [SerializeOrder(0)] public ChatChannel Channel { get; set; }

    /// <summary>The sender's nickname as stored (a guest has the neutral <c>guest-1234</c> form).</summary>
    [SerializeOrder(1)] public string FromNickname { get; set; } = string.Empty;

    /// <summary>The addressee's nickname for a whisper (the sender gets an echo); empty for the other channels.</summary>
    [SerializeOrder(2)] public string ToNickname { get; set; } = string.Empty;

    /// <summary>The cleaned, masked text.</summary>
    [SerializeOrder(3)] public string Text { get; set; } = string.Empty;

    /// <summary>Server time of sending, Unix milliseconds.</summary>
    [SerializeOrder(4)] public long SentAtMs { get; set; }
}
