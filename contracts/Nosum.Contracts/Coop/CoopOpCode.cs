// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Coop;

/// <summary>
/// Opcodes of the Coop domain.
/// <para>Co-operatives: membership and the shared warehouse (docs/adr/ADR-014). Band <c>0xA000-0xA0FF</c> is reserved for this domain. C2S requests use the lower half, S2C replies and pushes the upper half. Never reassign or reuse a value.</para>
/// </summary>
public static class CoopOpCode
{
    // -- C2S --

    /// <summary>Founds a co-op. Reply: <see cref="S2C.CoopResult"/>.</summary>
    public const ushort CreateCoop = 0xA000;

    /// <summary>Reads the player's co-op (or invitations). Reply: <see cref="S2C.CoopSnapshot"/>.</summary>
    public const ushort GetCoop = 0xA001;

    /// <summary>Invites a player on the same map (founder only). Reply: <see cref="S2C.CoopResult"/>.</summary>
    public const ushort InvitePlayer = 0xA002;

    /// <summary>Accepts or declines an invitation. Reply: <see cref="S2C.CoopResult"/>.</summary>
    public const ushort RespondInvite = 0xA003;

    /// <summary>Leaves the co-op. Reply: <see cref="S2C.CoopResult"/>.</summary>
    public const ushort LeaveCoop = 0xA004;

    /// <summary>Removes a member (founder only). Reply: <see cref="S2C.CoopResult"/>.</summary>
    public const ushort KickMember = 0xA005;

    /// <summary>Moves items from the inventory into the warehouse. Reply: <see cref="S2C.CoopResult"/>.</summary>
    public const ushort DepositItem = 0xA006;

    /// <summary>Moves items from the warehouse into the inventory. Reply: <see cref="S2C.CoopResult"/>.</summary>
    public const ushort WithdrawItem = 0xA007;

    /// <summary>Buys the next level of a co-op upgrade track from the warehouse stock (founder only, docs/adr/ADR-038). Reply: <see cref="S2C.CoopResult"/>.</summary>
    public const ushort UpgradeCoop = 0xA008;

    // -- S2C --

    /// <summary>Reply to every co-op request.</summary>
    public const ushort CoopResult = 0xA040;

    /// <summary>Reply to <see cref="GetCoop"/>; also pushed to every online member (and to an invited player) whenever it changes.</summary>
    public const ushort CoopSnapshot = 0xA041;
}
