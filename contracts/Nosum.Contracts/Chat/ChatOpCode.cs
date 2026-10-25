// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Chat;

/// <summary>
/// Opcodes of the Chat domain.
/// <para>Chat, block list and reports (docs/adr/ADR-029 D1, D3). Sub-range <c>0x9100-0x91FF</c> of the Trade band is reserved for this domain (docs/adr/ADR-029 D6). C2S requests use <c>0x9100-0x913F</c>, S2C replies and pushes <c>0x9140-0x917F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class ChatOpCode
{
    // -- C2S --

    /// <summary>Sends a chat message. Reply: <see cref="S2C.ChatResult"/>.</summary>
    public const ushort SendChat = 0x9100;

    /// <summary>Adds a player (by nickname) to the block list. Reply: <see cref="S2C.ChatResult"/>.</summary>
    public const ushort BlockPlayer = 0x9101;

    /// <summary>Removes a player (by nickname) from the block list. Reply: <see cref="S2C.ChatResult"/>.</summary>
    public const ushort UnblockPlayer = 0x9102;

    /// <summary>Asks for the block list. Reply: <see cref="S2C.BlockedList"/>.</summary>
    public const ushort GetBlocked = 0x9103;

    /// <summary>Reports a player, storing the last messages the reporter received as context. Reply: <see cref="S2C.ChatResult"/>.</summary>
    public const ushort ReportChat = 0x9104;

    // -- S2C --

    /// <summary>Reply to every chat request except <c>GetBlocked</c>.</summary>
    public const ushort ChatResult = 0x9140;

    /// <summary>Push to one player: a chat message.</summary>
    public const ushort ChatMessage = 0x9141;

    /// <summary>Reply to <c>GetBlocked</c>.</summary>
    public const ushort BlockedList = 0x9142;
}
