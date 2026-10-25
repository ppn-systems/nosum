// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Season;

/// <summary>
/// Opcodes of the Season domain (the server calendar, docs/adr/ADR-018).
/// <para>Band <c>0xD000-0xD0FF</c> is reserved for this domain. C2S requests use the lower half, S2C replies the upper half. Never reassign or reuse a value.</para>
/// </summary>
public static class SeasonOpCode
{
    // -- C2S --

    /// <summary>Asks for the server calendar. Reply: <see cref="S2C.CalendarInfo"/>.</summary>
    public const ushort GetCalendar = 0xD000;

    // -- S2C --

    /// <summary>Reply to <see cref="GetCalendar"/>.</summary>
    public const ushort CalendarInfo = 0xD040;
}
