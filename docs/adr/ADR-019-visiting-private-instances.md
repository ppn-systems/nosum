# ADR-019 — Visiting private instances

**Status:** Proposed (written by an agent; becomes Accepted when the owner approves it). Amends [ADR-015](ADR-015-private-instances-and-placeables.md) D4 (the access policy is no longer owner-only) and settles the pillar 3 question "how visiting works" ([pillars](../design/pillars.md)). The ADR numbers 017 and 018 belong to other work.

## Context

[ADR-015](ADR-015-private-instances-and-placeables.md) made the farm and the farmhouse one private copy per player and left a seam for visiting: `IInstanceAccessPolicy`, a `MapInstance` key that already names the owner,
and a presence group per instance. The owner now wants players to be able to visit each other: the owner keeps a list of players who may come in, co-op members ([ADR-014](ADR-014-coops-and-looks.md)) may come in unless the owner says no, and a visitor must not be able to
change what the owner has. Everything stays server-authoritative ([ADR-003](ADR-003-server-authoritative-websocket.md)): a client never names an instance, it only asks to visit a nickname, and every entry is checked again.

## Decision

- **D1 — The allow-list is persisted per owner.** `visit_grants (owner_id, visitor_id, created_at)` has the pair as its key, foreign keys to `players` with cascade delete (a deleted guest takes their rows with them), an index on `visitor_id` and a check that an owner is not their own visitor.
  `visit_settings (owner_id, coop_members_may_visit, updated_at)` holds the one setting; **no row means every default**, so the table stays empty until an owner changes something. The owner adds a player **by nickname** (case-insensitive, resolved on the server, so a typed
  name never becomes an id the client invented), removes them by account id from the snapshot the server sent. At most **50** players per list (`VisitRules.MaxListed`). Both tables come from one generated migration, `VisitingAllowList`.
- **D2 — Only registered players keep a list; guests can still visit.** A guest account is anonymous, throwaway and deleted when idle ([ADR-009](ADR-009-postgresql-persistence-and-accounts.md)): inviting people into it or being invited by it has no lasting value and would be the easiest abuse
  vector (make a guest, send invitations). So a guest cannot edit a list or the co-op setting (`GuestOwner`), and **a guest's farm cannot be visited** whatever rows exist (`VisitRules.MayEnter`). A guest *can* be put on a registered player's list (by the neutral name `guest-1234`, which the
  client lets the player type as "Khách 1234" / "Guest 1234") and can visit: a visitor only walks and waters (D3), so the risk is small, and a friend who has not registered yet can still be shown the farm. The grant survives when the guest registers (the account id does not change).
- **D3 — What a visitor may touch (safe default; owner question Q1).** `Nosum.Gameplay.Visits.VisitRules.MayDo(instance, player, action)` is the one table. In somebody else's farm or farmhouse a visitor may **walk, look, trade and take part in co-op invitations with the others present** (presence is
  the instance's, so those features need no change) and **water crops**. They may not till, plant, harvest, place, move or pick up furniture, or use the shipping bin. Enforcement is on the server in the services that change the world, never in the handlers or the client:
  `FarmService` (`FarmStatus.NotYourFarm`: a visitor holding anything but the watering can is refused before the plan is made, because empty hands would harvest; a plan that is not watering is refused after it), `PlaceableService` (`PlaceStatus.NotYourPlace` for place, move and pick up) and
  `MarketService.ShipAsync` (`MarketStatus.NotYourBin`). Each refusal has a test. Shared maps have no owner and nothing changes there.
- **D4 — Entering is a request by nickname, from a public map.** `VisitPlayer(nickname)` (reply `VisitResult`): the server resolves the nickname to the owner, asks the access policy and, if allowed, moves the player to `farm@<owner>` at the farm's spawn with the usual `MapChanged` push. The player must be on a **shared** map (Town, bus stop ...):
  from a private place they walk out first (`NotOnPublicMap`), so a visit cannot be chained and the saved place never points into somebody else's instance. `IPlayerService.TravelAsync(connection, instance)` is the new server-initiated move; it shares one `RelocateAsync` with walking onto an exit (leave, push, join, save), so the two cannot drift apart.
  Inside a visited place a door leads to **that owner's** copy of the next private map (the visitor can walk into the owner's farmhouse and back out); an exit to a public map leads to the public map, and the way back to a farm from there is the visitor's own. The saved place stays "map id + position" (ADR-015 D4): a visitor who disconnects returns to their own farm.
  The client never sends an instance, an owner id or a map for a visit.
- **D5 — Access is decided on every entry and revoked immediately.** `IInstanceAccessPolicy.CanEnterAsync` is now async; the host uses `VisitAccess` (the old `OwnerOnlyAccessPolicy` stays for tests). It reads the facts from PostgreSQL on **every** entry into a private place that is not the player's own (a visit or a door; nothing is cached, so nothing can be stale) and
  `VisitRules.MayEnter` decides: the owner, or listed, or (same co-op and the owner's setting is on), and never a guest's place. A permission can disappear in four ways and each one sends the affected visitors to the Town spawn **before the call returns**: removing the player from the list; turning the co-op setting off; the co-op ending a membership
  (`ICoopService.MembershipEnded`, raised after a leave or a kick, so a member who leaves is removed from every farm they entered through the co-op, and a founder who leaves takes the way out of their own members); and a final check right after a visitor has arrived (a revoke that raced a visit sees the player either in the presence group or not yet, and the check after the join covers the second case).
  The owner can also **kick**: send a visitor out without taking them off the list.
- **D6 — Presence and the offline owner (owner question Q3).** A visitor is in the owner's instance group like the owner would be, so the owner and the visitors see each other in the usual map snapshots, nicknames, looks, trade and co-op invitations. When the owner is **offline**, a listed player (or a co-op member) may still visit: the farm is loaded from PostgreSQL on first use, crops are computed from their timestamps
  ([ADR-011](ADR-011-inventory-farming-data-model.md)) and a watering counts. This is the default because pillar 3's seasons and a co-op that waters each other's fields are the point; the owner can remove the entry at any time.
- **D7 — Wire (band `0xE000`, append-only).** C2S `0xE000-0xE03F`: `GetVisitors`, `AddVisitor(nickname)`, `RemoveVisitor(player id)`, `KickVisitor(player id)`, `SetCoopVisits(allowed)`, `VisitPlayer(nickname)`; S2C `0xE040-0xE07F`: `VisitResult(VisitStatus)`, `VisitorsSnapshot(coop setting, is guest, rows)`; `VisitorRow` = account id, nickname, listed, present.
  Codes and ids only ([ADR-010](ADR-010-internationalization.md)). Three enum values were appended to existing status enums (`FarmStatus.NotYourFarm = 14`, `PlaceStatus.NotYourPlace = 14`, `MarketStatus.NotYourBin = 18`); no existing value moved and no field was appended to an existing packet, so **`NosumProtocol.Version` is not bumped by this change**.
  Handlers are `USER`-only, take the identity from the connection (so nobody can edit another player's list) and are rate limited (reads 4/s burst 5, edits 6/s burst 3, a visit 3/s burst 2).
- **D8 — Client.** A self-contained "Visitors" panel: `VisitChannel` (socket), `VisitorsViewModel` and `VisitorsPanel` (built only from the shared controls of [ADR-016](ADR-016-ui-theme-and-hud-layout.md)). It has two sections: visit a friend (type a nickname) and who may enter my place (add by nickname, the list with "here now" badges, send to Town, remove, the co-op toggle, and a guest notice). The only wiring outside the component is
  a toolbar entry, a `Home.razor` case and the DI lines. Refusals show as text from `Visit_Status_*`, `Farm_Status_NotYourFarm`, `Place_Status_NotYourPlace` and `Market_Status_NotYourBin` (vi + en).

## Owner questions

1. **Q1 — What may a visitor touch?** Built: walk, look, trade, water. Candidates to loosen: harvest a ripe crop (into whose bag? shared?), a per-friend "helper" level on the list, or letting a co-op member do more in a co-op farm; to tighten: no watering at all.
2. **Q2 — Does an invitation need consent in the moment** ("knock", the owner must be online and accept)? Built: no; being on the list is the consent, and the owner sees who is there.
3. **Q3 — May a listed player visit while the owner is offline?** Built: yes (D6).
4. **Q4 — Co-op members: on by default?** Built: on, as briefed; the owner can turn it off. Should a co-op's *founder* always be able to visit members' farms?
5. **Q5 — Guests.** Built: a guest can visit when listed, cannot keep a list, and cannot be visited (D2). Is that the right balance, or should guests be unable to visit too?
6. **Q6 — Getting there.** Built: a panel with a nickname box, from any public map. Alternatives: a gate or signpost per friend in the Town, or a "visit" action on the nearby-player list or a profile card. Should a visitor also have a "go home" button (they now walk out, or the owner sends them out)?
7. **Q7 — Crowding and manners.** No cap on visitors per farm, no block list, no notification when somebody arrives (the owner sees them in the farm). Do we need any of these?

## Alternatives considered

- **A warp from the Town into a friend's farm.** Fits [ADR-012](ADR-012-maps-and-warps.md) literally, but exits are static data from the maps and a friend list is per player; a request that names a nickname keeps the destination on the server and needs no map change.
- **Caching the allow-list in memory.** One query per entry is cheaper than the invalidation (revoke, co-op, guest upgrade) a cache needs; entries are rare compared to moves.
- **Letting visitors do more by default** (harvest, place). Rejected as the default: anything that changes what the owner has should be the owner's choice (Q1).
- **A separate "visitor" flag on the presence.** Not needed: `VisitRules.IsVisitor(instance, player)` is derived from the instance the server already keeps.
- **Revoking by polling.** The events that remove a permission are all server-side calls, so each one sweeps; there is no timer.

## Consequences

- A player's farm is no longer a place only they stand in; the ADR-015 consequence "two players are never in the same farm" holds only until somebody is invited. Features keyed by `PlayerPresence.Key` already work for several players in one instance.
- `IInstanceAccessPolicy.CanEnter` became `CanEnterAsync`; every entry into someone else's place costs up to four small reads.
- A revoke is a short critical section on the visitor: a player in the middle of a map change cannot be moved at that instant, so the sweep looks again (four tries, 100 ms apart) and the post-arrival check covers the rest.
- The co-op service gained an event (`MembershipEnded`); a listener that throws cannot undo a membership change.
- The farm's `last_worked_by` can now be a visitor (watering), which stays harmless for the migration that used it.

## Revisit when

The owner answers Q1-Q7, co-op land exists (the instance owner would be the co-op, and visiting would then be by membership), a second server process exists (the sweeps go through in-process presence), farms get crowded (a cap or interest management), or abuse needs a block list.
