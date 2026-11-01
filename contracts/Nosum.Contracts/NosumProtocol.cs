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
    /// 14: farm animals - <c>AnimalKind</c> values 3-6 (duck, goat, sheep, pig) appended; their produce items 906-917 appended to the item catalogue (docs/adr/ADR-028 amendment 2). No packet or field changed.
    /// 17: collection journal - new packets <c>GetJournal</c> and <c>JournalReply</c> (with <c>JournalEntry</c> rows), discoveries recorded on every item grant.
    /// 18: emotes and pet companion (docs/adr/ADR-075) - new packets <c>SendEmote</c>, <c>EmoteResult</c>, <c>EmoteShown</c>, <c>GetPet</c>, <c>AdoptPet</c>,
    /// <c>PetPet</c>, <c>PetState</c>; <c>PetKind</c> and <c>PetName</c> appended to <c>LookRow</c>.
    /// 20: fishing (docs/adr/ADR-078) - new packets <c>CastRod</c>, <c>ReelIn</c> (C2S <c>0xC300-0xC301</c>), <c>CastResult</c>, <c>ReelResult</c>
    /// (S2C <c>0xC340-0xC341</c>) and <c>FishingStatus</c>; <c>ToolKind.FishingRod</c>, <c>SkillKind.Fishing</c> and <c>CraftSkill.Fishing</c> are
    /// appended values, and <c>FishingXp</c> is stored on the player state. No existing packet changed. Version 19 is left unused.
    /// 21: fish size - <c>SizeCm</c> appended to <c>ReelResult</c> (docs/adr/ADR-078 amendment).
    /// 22: development QA hook (docs/adr/ADR-079) - new Development-only packets <c>DevTeleport</c>, <c>DevSeason</c>, <c>DevLevelUp</c>, <c>DevFishingBite</c> (C2S <c>0xF002-0xF005</c>) and their replies (S2C <c>0xF042-0xF045</c>). Production does not map them. No existing packet changed.
    /// 23: fishing skill row - <c>FishingXp</c> appended to <c>SkillXpChanged</c> (docs/adr/ADR-078 amendment). No packet, opcode or other field changed.
    /// 24: paid pets and rename scrolls (docs/adr/ADR-075 amendment, docs/adr/ADR-009 amendments) - new packet <c>RehomePet</c> (C2S <c>0x9503</c>); <c>PetStatus</c> gains
    /// <c>NotAtShop</c> and <c>NotEnoughCoins</c>, <c>AccountStatus</c> gains <c>NicknameNeedsScroll</c>. No existing packet or field changed.
    /// 25: backpack upgrades (docs/adr/ADR-083) - new packets <c>GetBackpack</c> and <c>UpgradeBackpack</c> (C2S <c>0x5004-0x5005</c>) and <c>BackpackState</c>
    /// (S2C <c>0x5043</c>), <c>BackpackStatus</c>. No existing packet, opcode or field changed.
    /// 26: footwear and the movement speed multiplier (docs/adr/ADR-084) - <c>FeetItemId</c> appended to <c>LookRow</c>; <c>WearSlot.Feet</c> (value 4) is a new wardrobe slot. No packet or opcode changed.
    /// 27: running - <c>MoveIntent.Running</c> appended (docs/adr/ADR-085). Clients built for 26 do not send the run flag, so the client and server ship together; no other packet changed.
    /// 28: thirsty crops - <c>Withered</c> appended to <c>FarmTileRow</c> (the server's withered flag, so the client no longer guesses it; docs/adr/ADR-021 D7 amendment 2026-10-31). Clients built for 27 misread the tile rows, so they ship together.
    /// 29: wide-use count - <c>Worked</c> appended to <c>UseItemResult</c>: the tiles a hoe or watering can line worked, each charged its own energy; and <c>FarmStatus.AlreadyWet</c> (24) appended, the refusal for soil that is wet already, which was <c>NothingToDo</c> (docs/adr/ADR-088). Clients built for 28 misread the reply, so they ship together.
    /// 30: market browse order and unit prices - <c>SortBy</c> appended to <c>BrowseListings</c> (a <c>MarketSort</c> value; docs/adr/ADR-013 amendment 2026-11-01). Server and client ship together (a field appended to an existing packet).
    /// 31: monster strike telegraph - new push <c>StrikeTelegraph</c> (S2C <c>0xC249</c>): an ordinary monster's wind-up before its melee strike lands (docs/adr/ADR-094). No existing packet changed.
    /// </summary>
    public const ushort Version = 31;
}
