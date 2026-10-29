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
    /// 5: boss enrage - <c>Enraged</c> appended to <c>MonsterRow</c> and <c>MonsterChanged</c>; new push <c>GroundTelegraph</c> (docs/adr/ADR-047).
    /// 6: market trends - <c>Average</c> appended to <c>PriceHistory</c> (docs/adr/ADR-030 D4).
    /// 7: co-op objectives - <c>Objectives</c> appended to <c>CoopSnapshot</c> (docs/adr/ADR-067).
    /// 8: bestiary - new packets <c>GetBestiary</c>, <c>BestiarySnapshot</c>, <c>EquipBestiaryTitle</c>, <c>EquipBestiaryTitleResult</c> (docs/adr/ADR-068).
    /// 9: Google and Microsoft sign-in only (docs/adr/ADR-069) - new packets <c>ExternalSignIn</c> and <c>LinkExternalIdentity</c>; <c>Register</c>,
    /// <c>Login</c>, <c>UpgradeGuest</c>, <c>ChangePassword</c> and <c>PasswordResult</c> retired (no fields); <c>AccountStatus</c> codes 20-21 added.
    /// 10: farm sprinklers and tended soil - <c>TendedDay</c> appended to <c>FarmTileRow</c>, <c>PlacedAtMs</c> appended to <c>PlacedObjectRow</c>, <c>FarmAction.CutWithered</c> (docs/adr/ADR-018, ADR-021, ADR-024).
    /// 11: buff slot - <c>Buff</c> appended to <c>VitalsState</c>; speed buffs (Swiftness, Brotherhood in Arms) live in their own slot apart from Regeneration and Poison (issues #132, #146).
    /// 12: held tool, trade lock, hotbar sort lock - <c>HeldItemId</c> appended to <c>PlayerState</c>; <c>ConfirmLockMs</c> appended to <c>TradeState</c>;
    /// <c>TradeStatus.TooSoon</c> (value 12); <c>LockHotbar</c> appended to <c>SortInventory</c>.
    /// 13: emotes and pet companion (docs/adr/ADR-071) - new packets <c>SendEmote</c>, <c>EmoteResult</c>, <c>EmoteShown</c>, <c>GetPet</c>, <c>AdoptPet</c>,
    /// <c>PetPet</c>, <c>PetState</c>; <c>PetKind</c> and <c>PetName</c> appended to <c>LookRow</c>.
    /// </summary>
    public const ushort Version = 13;
}
