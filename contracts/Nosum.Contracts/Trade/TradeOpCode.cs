// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Trade;

/// <summary>
/// Opcodes of the Trade domain.
/// <para>Direct player-to-player trade (docs/adr/ADR-013). Band <c>0x9000-0x90FF</c> is reserved for this domain. C2S requests use the lower half, S2C replies and pushes the upper half. Never reassign or reuse a value.</para>
/// </summary>
public static class TradeOpCode
{
    // -- C2S --

    /// <summary>Asks a player on the same map to trade. Reply: <see cref="S2C.TradeResult"/>.</summary>
    public const ushort TradeRequest = 0x9000;

    /// <summary>Accepts or declines a trade invitation. Reply: <see cref="S2C.TradeResult"/>.</summary>
    public const ushort TradeRespond = 0x9001;

    /// <summary>Replaces the player's side of the open trade; both confirmations are cleared. Reply: <see cref="S2C.TradeResult"/>.</summary>
    public const ushort TradeSetOffer = 0x9002;

    /// <summary>Confirms the offers as they stand. Reply: <see cref="S2C.TradeResult"/>.</summary>
    public const ushort TradeConfirm = 0x9003;

    /// <summary>Cancels the open trade (or declines an invitation). Reply: <see cref="S2C.TradeResult"/>.</summary>
    public const ushort TradeCancel = 0x9004;

    // -- S2C --

    /// <summary>Reply to every trade request.</summary>
    public const ushort TradeResult = 0x9040;

    /// <summary>Push to one player: another player asks to trade.</summary>
    public const ushort TradeInvite = 0x9041;

    /// <summary>Push to both parties: the open trade as it stands.</summary>
    public const ushort TradeState = 0x9042;

    /// <summary>Push to both parties: the trade ended.</summary>
    public const ushort TradeEnded = 0x9043;
}
