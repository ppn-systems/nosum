// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts;

/// <summary>
/// Wire protocol identity shared by the server and every client.
/// </summary>
public static class NosumProtocol
{
    /// <summary>
    /// Wire protocol version. Bump only on a breaking wire change (a removed or reordered
    /// packet member, a reassigned opcode, a field appended to an existing packet); server and clients then ship together.
    /// 3: nicknames - fields appended to <c>Register</c>, <c>UpgradeGuest</c>, <c>AccountResult</c>, <c>LookRow</c>, <c>ListingRow</c>, <c>CoopMemberRow</c>, <c>CoopInviteRow</c> (docs/adr/ADR-009 amendments).
    /// 4: co-op upgrades - <c>WarehouseLevel</c> and <c>LandLevel</c> appended to <c>CoopSnapshot</c> (docs/adr/ADR-038).
    /// 5: market trends - <c>Average</c> appended to <c>PriceHistory</c> (docs/adr/ADR-030 D4).
    /// </summary>
    public const ushort Version = 5;
}
