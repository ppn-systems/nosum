// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Nosum.IntegrationTests.Server.DbBudget;

/// <summary>
/// The most database work a hot-path packet may cost today (docs/adr/ADR-035). A limit is the number measured on the branch that introduced it;
/// the follow-up changes of the ADR lower the numbers, and every lowered number is written down here in the same commit, so a regression
/// fails <see cref="DbBudgetTests"/>. Never raise a limit to make a test pass: a packet that got more expensive is the bug.
/// </summary>
internal static class DbBudgetBaseline
{
    /// <summary>The limits by row key (<c>Name / Variant</c>): reads, writes, transactions, SaveChanges calls.</summary>
    public static IReadOnlyDictionary<string, Limit> HotPaths { get; } = new Dictionary<string, Limit>
    {
        ["AddVisitor / allow-list by nickname"] = new(0, 1, 0, 1),
        // Amended 2026-10-31 (docs/adr/ADR-035, Amendment): adopting a pet now completes the adopt_pet tutorial step, so the quest copy is read,
        // the completed row is written and the 40-coin reward is paid in one transaction. The cost is the step, not a regression.
        // Amended 2026-10-31 (docs/adr/ADR-035, Amendment; docs/adr/ADR-075 amendment): adopting is a purchase at the carpenter's counter, paid through the
        // transfer service in the same transaction as the pet row, as a shop buy is (BuyFromShop 4/3/3/3). The increase is the price of paying, not a leak.
        ["AdoptPet / adopt a cat"] = new(5, 4, 3, 4),
        // Amended 2026-11-01 (docs/adr/ADR-092 D2): the first petting of a game day may bring back a forage item, added to the bag in its own
        // transaction after the petting is saved. The probe's first petting finds one, so the cost is the item's write, not a leak.
        ["PetPet / pet the cat"] = new(1, 2, 1, 2),
        ["PetPet / pet the cat, asked again"] = new(1, 0, 0, 0),
        // Measured on the branch that adds it (docs/adr/ADR-075 amendment): the pet is given back for half its price in one transfer.
        ["RehomePet / give the cat back"] = new(3, 1, 1, 1),
        ["GetPet / no pet"] = new(1, 0, 0, 0),
        ["GetPet / no pet, asked again"] = new(1, 0, 0, 0),
        // Amended 2026-10-31 (docs/adr/ADR-035, Amendment): the first emote completes the send_emote tutorial step, so the quest copy is read,
        // the completed row is written and the reward is paid in one transaction. Later emotes cost what they did.
        ["SendEmote / wave"] = new(1, 1, 1, 1),
        ["SendEmote / wave, chain finished"] = new(0, 0, 0, 0),
        ["BlockPlayer / block by nickname"] = new(4, 1, 0, 1),
        ["BrowseListings / empty market, page 0"] = new(2, 0, 0, 0),
        ["BrowseListings / empty market, page 0, asked again"] = new(2, 0, 0, 0),
        ["BrowseListings / one listing on the page"] = new(2, 0, 0, 0),
        ["BuyFromShop / buy seeds"] = new(3, 3, 3, 3),
        // Lowered 2026-11-01 (docs/adr/ADR-035 amendment 2026-11-01; docs/adr/ADR-013 A9): the listing is read once under the listing lock and the save is a
        // compare-and-set on its status; the two wallets come in one query. 6 reads become 4: the two mail reads are the sale notice.
        ["BuyListing / buy one listing"] = new(4, 2, 1, 2),
        ["CancelListing / cancel own listing"] = new(3, 1, 1, 1),
        ["CastRod / at the sea with a rod"] = new(0, 1, 0, 1),
        ["ChestMove / five parsnips into the chest"] = new(1, 1, 1, 1),
        ["ChestMove / one parsnip back out"] = new(1, 1, 1, 1),
        ["Craft / a chest from wood"] = new(2, 3, 3, 3),
        ["Craft / five fences"] = new(0, 1, 1, 1),
        ["CreateCoop / found a co-op"] = new(7, 1, 1, 1),
        ["CreateListing / first listing"] = new(3, 1, 1, 1),
        ["CreateListing / second listing"] = new(3, 1, 1, 1),
        // Amended 2026-10-31 (docs/adr/ADR-035, Amendment): the first request filled completes the fill_request tutorial step, so the quest copy is
        // read, the completed row is written and the reward is paid in one transaction on top of the request itself.
        ["DeliverRequest / fill one request"] = new(6, 3, 3, 3),
        ["DeliverRequest / fill one request, chain finished"] = new(4, 1, 1, 1),
        ["DepositItem / into the warehouse"] = new(6, 1, 1, 1),
        ["Disconnect / joined player, socket closed"] = new(0, 1, 0, 1),
        ["Eat / a parsnip from the hotbar"] = new(0, 0, 0, 0),
        ["ExternalSignIn / Google identity, new account"] = new(2, 1, 1, 1),
        ["ExternalSignIn / Google identity, registered player"] = new(3, 1, 1, 1),
        ["GetBlocked / no blocks"] = new(1, 0, 0, 0),
        ["GetBlocked / no blocks, asked again"] = new(1, 0, 0, 0),
        ["GetBlocked / one block"] = new(1, 0, 0, 0),
        ["GetCalendar / calendar"] = new(0, 0, 0, 0),
        ["GetCalendar / calendar, asked again"] = new(0, 0, 0, 0),
        ["GetCoop / an invitation waiting"] = new(3, 0, 0, 0),
        ["GetCoop / no co-op"] = new(2, 0, 0, 0),
        ["GetCoop / no co-op, asked again"] = new(1, 0, 0, 0),
        ["GetCoop / two members, stock and log"] = new(3, 0, 0, 0),
        ["GetFarm / empty farm"] = new(0, 0, 0, 0),
        ["GetFarm / empty farm, asked again"] = new(0, 0, 0, 0),
        ["GetFriends / no friends"] = new(3, 0, 0, 0),
        ["GetFriends / no friends, asked again"] = new(0, 0, 0, 0),
        ["GetFriends / one friend"] = new(0, 0, 0, 0),
        ["GetFriends / one request waiting"] = new(0, 0, 0, 0),
        ["GetGroundItems / nothing on the ground"] = new(0, 0, 0, 0),
        ["GetGroundItems / nothing on the ground, asked again"] = new(0, 0, 0, 0),
        ["GetInventory / starter kit"] = new(0, 0, 0, 0),
        ["GetInventory / starter kit, asked again"] = new(0, 0, 0, 0),
        ["GetJournal / read the journal"] = new(1, 0, 0, 0),
        ["GetLooks / own and seen players"] = new(2, 1, 0, 1),

        ["GetLooks / own and seen players, asked again"] = new(0, 0, 0, 0),
        ["GetMine / in the entrance cave"] = new(1, 0, 0, 0),
        ["GetMyListings / none"] = new(1, 0, 0, 0),
        ["GetMyListings / none, asked again"] = new(1, 0, 0, 0),
        ["GetMyListings / one listing"] = new(1, 0, 0, 0),
        ["GetNodes / farm"] = new(0, 0, 0, 0),
        ["GetNodes / farm, asked again"] = new(0, 0, 0, 0),
        ["GetPlaced / a chest and a furnace standing"] = new(0, 0, 0, 0),
        ["GetPlaced / empty farm"] = new(0, 0, 0, 0),
        ["GetPlaced / empty farm, asked again"] = new(0, 0, 0, 0),
        ["GetPriceHistory / no sales"] = new(1, 0, 0, 0),
        ["GetPriceHistory / no sales, asked again"] = new(1, 0, 0, 0),
        ["GetQuests / new player"] = new(1, 1, 1, 1),
        ["GetQuests / new player, asked again"] = new(0, 0, 0, 0),
        ["GetRequests / today's board"] = new(3, 0, 0, 0),
        ["GetRequests / today's board, asked again"] = new(2, 0, 0, 0),
        ["GetVisitors / no visitors"] = new(1, 0, 0, 0),
        ["GetVisitors / no visitors, asked again"] = new(0, 0, 0, 0),
        ["GetVisitors / one visitor on the farm"] = new(0, 0, 0, 0),
        ["GetGuestbook / read the guestbook"] = new(2, 0, 0, 1),
        ["GetWallet / wallet"] = new(1, 1, 1, 1),
        ["GetWallet / wallet, asked again"] = new(1, 0, 0, 1),
        ["InvitePlayer / invite a neighbour"] = new(7, 2, 0, 2),
        ["InvitePlayer / invite again"] = new(6, 2, 0, 2),
        ["JoinMap / first join, no saved place"] = new(9, 2, 2, 2),
        ["JoinMap / returning player, saved place"] = new(3, 0, 0, 0),
        ["KickMember / owner removes a member"] = new(10, 1, 0, 1),
        ["KickVisitor / send the visitor away"] = new(0, 1, 0, 1),
        ["LeaveCoop / a member leaves"] = new(10, 1, 0, 1),
        ["LinkExternalIdentity / guest to registered"] = new(4, 1, 1, 1),
        ["MoveIntent / one step, same chunk"] = new(0, 0, 0, 0),
        ["MoveIntent / step inside a chunk, another player in sight"] = new(0, 0, 0, 0),
        ["MoveIntent / step into a new chunk (interest set changes)"] = new(0, 0, 0, 0),
        ["MoveIntent / step onto an exit: change of map"] = new(2, 3, 1, 3),
        ["MoveSlot / split a stack into a free slot"] = new(0, 1, 1, 1),
        ["PickUpObject / pick up a fence"] = new(2, 1, 1, 1),
        // Amended 2026-10-31 (docs/adr/ADR-035, Amendment): every placement now counts toward the decorate tutorial step, which is one quest row
        // write (no read: the copy is held) on each placement until the third one completes the step.
        ["PlaceObject / a chest"] = new(0, 2, 1, 2),
        ["PlaceObject / a chest, chain finished"] = new(0, 1, 1, 1),
        // Amended 2026-10-31 (docs/adr/ADR-035, Amendment): the first coop placed completes the build_coop tutorial step, so the quest copy is read,
        // the completed row is written and the reward is paid in one transaction on top of the placement.
        ["PlaceObject / a coop blueprint"] = new(5, 4, 3, 3),
        // Amended 2026-10-31 (docs/adr/ADR-035, Amendment): every placement counts toward the decorate tutorial step (see the chest row).
        ["PlaceObject / a fence"] = new(1, 2, 2, 2),
        // Amended 2026-10-31 (docs/adr/ADR-035, Amendment): every placement counts toward the decorate tutorial step (see the chest row).
        ["PlaceObject / a furnace"] = new(0, 2, 1, 2),
        ["ReadMail / mark read"] = new(2, 0, 0, 0),
        ["ReelIn / before the bite"] = new(0, 0, 0, 0),
        ["RemoveFriend / unfriend"] = new(1, 1, 1, 1),
        ["RemoveVisitor / take off the allow-list"] = new(1, 1, 0, 1),
        ["RequestFriend / by nickname"] = new(8, 1, 0, 1),
        ["RespondFriend / accept"] = new(1, 1, 1, 1),
        ["RespondInvite / accept"] = new(8, 1, 1, 1),
        ["RespondInvite / accept again"] = new(8, 1, 1, 1),
        ["ResumeSession / token, no other session"] = new(3, 1, 1, 1),
        ["ResumeSession / token, replacing an open session"] = new(3, 1, 1, 1),
        ["SelectSlot / hotbar slot 1"] = new(0, 0, 0, 0),
        ["SendChat / local channel"] = new(0, 0, 0, 0),
        ["SendChat / map channel"] = new(1, 0, 0, 0),
        ["SendChat / map channel, one listener has blocked the sender"] = new(1, 0, 0, 0),
        ["SendChat / whisper by nickname"] = new(2, 0, 0, 0),
        ["SetCoopVisits / co-op members may visit"] = new(1, 0, 0, 1),
        ["SetFriendsVisit / allow friends to visit"] = new(1, 1, 0, 1),
        ["ShipItems / ship a stack at the bin"] = new(3, 3, 3, 3),
        ["StartGuest / new guest"] = new(1, 1, 1, 1),
        ["TradeConfirm / first side confirms"] = new(0, 0, 0, 0),
        ["TradeConfirm / second side confirms: the swap"] = new(2, 1, 1, 1),
        ["UnblockPlayer / unblock"] = new(1, 1, 0, 1),
        ["UpgradeCoop / warehouse to level 1"] = new(11, 1, 1, 1),
        ["UpgradeTool / hoe to copper"] = new(4, 1, 1, 1),
        ["UseItem / break a one-hit node"] = new(4, 3, 3, 3),
        ["UseItem / harvest a mature crop"] = new(5, 4, 3, 4),
        ["UseItem / plant a seed"] = new(1, 2, 1, 2),
        ["UseItem / plant on untilled soil (refused)"] = new(0, 0, 0, 0),
        ["UseItem / slot with nothing in it (refused)"] = new(0, 0, 0, 0),
        ["UseItem / till (hoe)"] = new(3, 3, 3, 3),
        ["UseItem / water"] = new(2, 2, 2, 2),
        ["UseItem / water on a new game day"] = new(1, 1, 1, 1),
        ["UseItemArea / watering can over a block of tiles"] = new(0, 0, 0, 0),
        ["VisitPlayer / friend visits the farm"] = new(8, 2, 1, 2),
        ["WithdrawItem / out of the warehouse"] = new(7, 1, 1, 1),
        ["WriteBehindFlush / a hotbar choice waiting"] = new(0, 1, 0, 1),
    };

    /// <summary>The limit of one row.</summary>
    /// <param name="Reads">Most SELECT commands.</param>
    /// <param name="Writes">Most INSERT/UPDATE/DELETE commands.</param>
    /// <param name="Transactions">Most explicit transactions.</param>
    /// <param name="SaveChanges">Most SaveChanges calls.</param>
    internal readonly record struct Limit(int Reads, int Writes, int Transactions, int SaveChanges);
}
