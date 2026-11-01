# Critique: social play (friends, chat, emotes, visits, co-op, trade, mail)

Status: review, 2026-11-01. Branch `claude/rebuild-social`. Reviewer's findings; the owner decides which backlog items to take.
Companion to [`pillars.md`](pillars.md) (pillar 1, co-ops; pillar 2, trade), [`ui-standard.md`](ui-standard.md) and
[`research-hud-and-loops.md`](research-hud-and-loops.md). Frames are in [`../qa/rebuild-social/`](../qa/rebuild-social/):
`before/` is the code on `master`, `after/` is this branch; the grading is in [`../qa/rebuild-social/README.md`](../qa/rebuild-social/README.md).

## How this was played (and what was not)

- Local Development server (`DOTNET_ENVIRONMENT=Development`, `server/Nosum.Host`) and a WASM client built with `NosumDevTools=true`.
  Playwright ran two desktop guests at 1280×720 and one iPhone 14 profile (390×844) next to a desktop guest. The QA hook
  (`nosumQa.teleport`) put them in the Town, where players meet; the start is a private farm.
- **Guests only.** Development has no Google or Microsoft sign-in (ADR-069), so no registered account could be made. The
  server gives guests no friends list and refuses a visit to a guest's farm (`Visit_Status_GuestOwner`). So friend requests,
  the friends list with its presence, and visits to a friend's farm were **not** played in the browser. The friends points
  below come from the code and from unit tests, and say so.
- Not exercised beyond the frames listed: a full trade with items and a confirm; mail with attachments; phone keyboard
  behaviour on a real iOS or Android device (Playwright's phone profile is a desktop Chromium with touch emulation).

## Findings

Severity: **blocker** (a player cannot do the thing at all), **high** (possible, but the player is stuck or misled),
**medium** (possible, but costs time or clarity), **low**. Status: **fixed here** (with the check that proves it) or **backlog**.

| # | Area | Finding | Evidence | Sev. | Status |
| :-- | :-- | :-- | :-- | :-- | :-- |
| 1 | Chat, keyboard | **Enter never opens the chat box on any screen.** The emote wheel (#233) puts an always-present `.emote-layer` in `.hud-overlay`; the Enter guard in `chat-input.js` treats any `.hud-overlay > *` as an open dialog and ignores Enter. Chat was silently unusable for keyboard players. | `tests/browser/chat-check.js` on the old build: `box opens with Enter false`, `sent line is shown false`, `RESULT: FAIL`. On a fresh farm the input count after Enter is 0. | blocker | **fixed** (`chat-input.js`): the guard ignores `.emote-layer`. `chat-check.js`: `RESULT: PASS` on this build. |
| 2 | Chat, phone | **A phone cannot open chat.** The touch layer draws only Use and Run; phones have no Enter key. A phone player who wants to say hello has no way to do it. | `before/…-390x844-touch-buttons` log: touch buttons `Dùng`, `Chạy` (vi) and `Use`, `Run` (en); `no chat button on the touch layer`. | blocker | **fixed**: a Chat button on the touch layer (`TouchControls`), shown where Enter would open the box and hidden while a merchant's box is open. The `after/` log: `chat button tapped: input box opened = true` in both languages. |
| 3 | Phone, reach | On a phone the column holds only Menu (plus Emotes, Mail, Settings). Trade and Friends are tiles two taps down, and Co-op is not in the menu at all. | `before/en-390x844-menu.png` (tiles: Inventory, Crafting, Trade, Visitors, Friends, Skills, Pet, Profile, World map, Journal; no Co-op). | high | backlog. A one-tap phone door for Trade needs a layout decision (ui-standard section 6). |
| 4 | Co-op, discovery | **Co-op opens only from the Town board keeper's dialog** ("Goals / Co-op / Talk / Leave"), never from the menu or the column. A new player has no reason to walk to the board. ADR-081 chose this on purpose; the cost is that most players will not find it. | `before/en-1280x720-coop-none.png` is the first frame after the dialog. The desktop menu tabs and the phone menu list have no Co-op tile. | high | backlog (needs a decision: a menu tile, or a hint from the Town board's quest line). |
| 5 | Co-op, meeting | **Invites need a partner on the same public map.** Each farm is private, so two players who want a co-op must first meet in the Town. The founder sees "No other players here to invite." and the picker lists only nearby players. | `before/en-1280x720-coop-founded.png` (the message); trade on a farm: "No other players on this map." (walk log). | high | backlog (design: an invite by nickname, or an invite that waits for the partner). |
| 6 | Co-op, value | **The warehouse log is a list of raw lines**, so a member cannot see who has put in what. Pillar 1 says "every contribution is recorded so members can see who did what"; the lines do record it, but nobody reads them. | Code: `CoopViewModel.Log` builds one line per entry; the server sends only `CoopRules.RecentLogLines`. | medium | **fixed**: each member's row shows `N deposited` (deposits only; upgrade costs are not gifts), and a note says the counts cover the recent log. Frames: `after/en-1280x720-coop-contributions.png`, `after/vi-1280x720-coop-contributions.png`. Unit-tested (`CoopContributionsTests`). |
| 7 | Co-op, invite | After an invite is sent, the picker still lists the invited player with **Invite** as if nothing happened. | Walk log: after the invite, A's Co-op page still shows `Guest 0603 - Invite` beside the Members list. | medium | backlog (needs the pending invites from the server, or a "sent" mark on the client). |
| 8 | Friends, visit | **An online friend cannot be visited from the list.** The row shows the place ("On Town" or "Online") and a Remove button, but a visit meant leaving the page for the Visitors tab and typing the nickname again. | Code: `FriendsPanel` had no visit action. | medium | **fixed**: a **Visit farm** button on each friend row (`FriendsViewModel.Visit`, the same `VisitPlayer` packet the Visitors page sends). An accepted visit closes the menu, as the Visitors page does; a refusal shows its reason. Unit-tested (4 tests). **Not browser-played** (guests have no friends). |
| 9 | Layout, trade and co-op grids | **The item grid is clipped at both edges.** In the trade window and the co-op warehouse the first and last columns are cut in half. This breaks ui-standard checklist items 6 and 8 ("nothing is clipped", "item icons are whole"). It is older than this branch. | `before/en-1280x720-trade-open-sender.png`; `before/en-1280x720-coop-founded.png` (first column of the bag). | medium | backlog (shared `ItemGrid` width; both trade and co-op use it). |
| 10 | Menu column | The column is **icon-only**. A name shows only on hover (the tooltip `Trade [T]` appeared on hover in an exploration run; that frame is not in the set). A touch player has no hover, so the eight icons cannot be told apart. | `before/en-1280x720-town-hud.png`: eight unlabelled icons. Aria labels exist; no visible text. | low | backlog. |
| 11 | Trade, safety and clarity | **Credit where due.** The invitation says who is asking and has Accept and Decline. The open trade shows each side's coins and a **Not confirmed** line for each side, Confirm and Cancel, and a confirm lock after each change (`TradeViewModel.ConfirmLocked`). | `before/en-1280x720-trade-invite-receiver.png`, `before/en-1280x720-trade-open-sender.png`. | info | no change. Not played: items moved and confirmed. |
| 12 | Emotes | The wheel opens with B (or the Emotes button on a phone) and shows the twelve emotes around the farmer. | `before/en-1280x720-emote-wheel.png`, `before/en-390x844-emote-wheel.png`. | info | no change. |
| 13 | Presence | Co-op members show **Online / Offline** beside each name. Friends show Online / "On <place>" / Offline (code; not browser-played). | `before/en-1280x720-coop-founded.png` (Founder, Online). | info | no change. |
| 14 | Escape | With no panel open, Escape opens the last-used menu tab (`Home.razor`, "open it on the tab used last"). After a chat line, a player who presses Escape to close the box and then Escape again lands in the inventory. | Observed on the old build (the Escape after a send opened `Close / Sort`). | low | backlog (design). Not changed. |

## What the pillars need, and where the game is

- **Pillar 1 (co-ops).** The co-op page already shows members, upgrades and shared objectives (`before/en-1280x720-coop-founded.png`).
  What was missing is the contribution signal (finding 6, fixed) and a way to get there (finding 4, backlog). A co-op is
  also hard to start (finding 5): the people who would found one cannot meet except in the Town.
- **Pillar 2 (trade).** The direct trade is clear and has a real safety step (finding 11). Its grid is clipped (finding 9).
- **Pillar 3 (seasons and goals).** Not part of this review.

## Phone

A phone is a supported client (`Device:BlockTouchDevices` is `false`). Before this branch it could walk, farm, emote and trade
(through the Menu), but it could not talk. Finding 2 was the worst social gap in the game for a phone. The Chat button is
the smallest change that closes it; the layout of the rest of the phone's social doors is finding 3.

## Backlog, in the order the reviewer would take it

1. Co-op entry point (finding 4): a menu tile or a visible hint from the Town board.
2. Invite by nickname, and a pending state for an invite (findings 5 and 7).
3. Item grid width in trade and co-op (finding 9); this is a visible rule break.
4. Phone door for Trade and Friends (finding 3), and visible labels or a text line for the column (finding 10).
5. Escape after a chat line (finding 14).

## Round 3 (2026-11-01)

Status of the findings above after the round-3 pass (docs/qa/social-round3/README.md):

- **Finding 7 (invite still offered after joining): fixed.** The invite picker leaves out current members (`CoopInvitePicks`, unit-tested).
  The after frame reads "No other players here to invite."
- **Finding 8 (visit farm): browser-played.** A test friend (ADR-105) makes the two-player check possible; the visit button is clicked and the farm
  is shown. A database record of the visit is not checked yet.
- **Finding 9 and the friends row:** a nickname wrapped mid-word in the desktop friends row; the name now takes its own line.
- **Blocker found on master, not in the critique:** the renderer module declared `fitLabel` twice, so the page did not start on master. Fixed.
- **Still in the backlog:** finding 4 (co-op entry point), 3 (phone door for Trade and Friends), 5 (invite by nickname), 10 (labels on the column),
  14 (Escape after a chat line).
