// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Admin;

/// <summary>
/// Admin protocol opcodes (docs/adr/ADR-031 D7). Slice <c>0x1100-0x11FF</c> of the Account band (C2S requests <c>0x1101-0x117F</c>,
/// S2C replies <c>0x1181-0x11FF</c>). Never reassign or reuse a value. Every handler needs the admin permission level and is audited.
/// </summary>
public static class AdminOpCode
{
    /// <summary>Ends a player's connection.</summary>
    public const ushort Kick = 0x1101;

    /// <summary>Mutes a player's chat for some hours.</summary>
    public const ushort Mute = 0x1102;

    /// <summary>Bans a player.</summary>
    public const ushort Ban = 0x1103;

    /// <summary>Lifts a ban.</summary>
    public const ushort Unban = 0x1104;

    /// <summary>Gives a player items or coins.</summary>
    public const ushort GrantItem = 0x1105;

    /// <summary>Asks for the newest chat reports.</summary>
    public const ushort ListReports = 0x1106;

    /// <summary>Asks for one chat report's stored context.</summary>
    public const ushort ReadReport = 0x1107;

    /// <summary>The reply to <see cref="Kick"/>, <see cref="Mute"/>, <see cref="Ban"/>, <see cref="Unban"/> and <see cref="GrantItem"/>.</summary>
    public const ushort AdminResult = 0x1181;

    /// <summary>The reply to <see cref="ListReports"/>.</summary>
    public const ushort AdminReportList = 0x1182;

    /// <summary>The reply to <see cref="ReadReport"/>.</summary>
    public const ushort AdminReportDetail = 0x1183;
}
