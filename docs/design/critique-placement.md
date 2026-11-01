# Placement critique (furniture, the farmhouse)

Critique of the placement flow ([ADR-015](../adr/ADR-015-private-instances-and-placeables.md) D7–D10, [ADR-073](../adr/ADR-073-furniture-catalogue-and-decorating.md) D6) played in the browser on `claude/rebuild-placement`, graded against [`ui-standard.md`](ui-standard.md). Frames are in [`docs/qa/rebuild-placement/`](../qa/rebuild-placement/): `before/` is the code on `master`, `after/` is this branch. Both sets come from [`tests/browser/placement-capture.cjs`](../../tests/browser/placement-capture.cjs) (the "before" set was taken with an earlier, shorter run of the same steps, frames 00–06).

## Scope and method

- Played as a guest with the Development QA hook (`-p:NosumDevTools=true`, `DevTools.Grant`): the farmhouse, the chair (item 300), place, rotate, move, a refused spot, stow. Desktop 1280×720 and phone 390×844 (touch emulation), Vietnamese and English.
- **Not played this session:** the carpenter (ordering and placing a farm building), chests and furnaces, and sprinklers. The building flow below is from reading the code, not from play. The E/F/Q/R paths are the same for every placeable, so the findings on preview and touch apply to them too.

## Findings

Severity: **H** a player is blocked or loses something; **M** a player is confused or slowed; **L** cosmetic. "Fixed" means this branch changes it; "Open" means it is recorded for a follow-up.

| # | Sev | Finding | Frames (before → after) | Status |
| :-- | :-- | :-- | :-- | :-- |
| 1 | M | **A valid ghost looks like a placed piece.** The held piece is drawn in full colour on a valid tile, so "can I place here?" has no answer until E is pressed. | `before/desktop-en/02-ghost-open-floor.png` | **Fixed:** the valid ghost is green, the invalid one a stronger red. `after/desktop-en/02-ghost-open-floor.png` |
| 2 | M | **An invalid ghost is red with no reason.** The reason only comes back as a refusal after E, and then on the message line. | `before/desktop-en/05-ghost-over-bed-or-table.png` (red ghost, the farmer standing on it, no reason on screen) | **Fixed:** the strip's notice line shows the reason before the press, using the server's own `Place_Status_*` sentences. `after/desktop-en/05-ghost-over-bed-or-table.png` ("You are standing there."), `after/desktop-vi/05-…` (Vietnamese fits in the strip) |
| 3 | M | **After a placement the held piece stays on top of the one just placed,** and the overlap reads as red, so the new piece looks broken or refused. | `before/desktop-en/03-after-place.png` (red chair on the placed chair) | **Open.** The held item should step to the next free tile, or the ghost should hide until it moves. Not changed here: it is the placement loop itself, not a visual. |
| 4 | H (touch) | **No touch "Place".** The strip's E cap was a label, not a button; a phone player had only the generic "Use" button, which is not named for placing. | `before/phone-en/02-ghost-open-floor.png` (E cap is plain text; the bottom-right button says "Use") | **Fixed:** every cap is a button, and E places (`after/phone-en/07-tapped-place.png`: tapping Place sent the request and the notice reports the refusal). |
| 5 | M | **Picking a piece up gives no confirmation.** Stow (Q) moves the piece into the bag silently; the only sign is the bag count. | `before/desktop-en/06-after-stow.png` (no message, count changes) | **Fixed:** "Stowed in your bag. You can place it again any time." on success (`after/phone-en/08-stow-confirmation.png`, `after/desktop-en/08-stow-confirmation.png`). |
| 6 | M | **Q with no piece in front does nothing, and says nothing.** | `before/desktop-en/06-after-stow.png` | **Open.** Suggest a message "Không có gì phía trước" / "Nothing in front of you". |
| 7 | H | **Building placement cannot be undone.** A finished farm building cannot be moved or stowed (server: `Fixed`), and one E press places it. | server `PlaceableService` refuses `Fixed`; no confirmation on the client | **Fixed:** the first press on a valid building arms a confirmation shown on the strip line ("…cannot be moved or stowed. Press E again to build here."); a second press on the same tile within 6 s builds. Unit-tested (`BuildConfirmationTests`); **not browser-played** (no carpenter in this session). |
| 8 | M | **The furniture panel hides the room.** Left third of the room at 1280×720; most of it on the phone. The room is what the player is arranging. | `before/desktop-en/02-ghost-open-floor.png`, `before/phone-en/02-ghost-open-floor.png` | **Open** (UI standard §2, §3: the panel is in the centre region, but it covers the world it edits). A follow-up should make it collapsible or move the list to a sheet at phone width. |
| 9 | L | **"Furniture" title overlaps the category chips** (the title's lower half sits under the chips). | `before/desktop-en/02-ghost-open-floor.png` | **Open** (UI standard §3, check 6). |
| 10 | L | **"Stow" is clipped at the strip's right edge** ("Sto" + cut). | `before/desktop-en/02-ghost-open-floor.png`, `after/desktop-en/02-ghost-open-floor.png` | **Open** (UI standard check 6). |
| 11 | M | **The message line is clipped by the hotbar** at 1280×720: the second line of a toast sits behind the hotbar frame. Pre-existing; the new stow line shows it too. | `after/desktop-vi/08-stow-confirmation.png` (the Vietnamese line sits behind the hotbar frame) | **Open** (UI standard §2 "toast line above the hotbar"). |
| 12 | M | **On the phone, the menu column overlaps the furniture panel** (the right-hand icons cover the end of the category row, "Plants"/"Pets"). | `before/phone-en/02-ghost-open-floor.png` | **Open** (UI standard §6: the menu collapses to one "Menu" button at 390×844; it does not here, in this window). |
| 13 | L | **A farm hint shows in the farmhouse** ("Select the hoe and press the use key…") while the player is arranging furniture. | `before/desktop-en/02-ghost-open-floor.png` | **Open** (onboarding hint should follow the map). |

## Preview clarity (valid / invalid)

- Valid: green ghost (finding 1). Invalid: red ghost plus the reason in the strip (finding 2). The reason uses the same resource sentences the server refuses with, so the client and the server never disagree about the words.
- Still unclear: a ghost that sits on a placed piece right after placing (finding 3).

## Rotate and move controls

- R rotates and F picks up the piece in front and then places it with E. All four keys are buttons now (finding 4), so the caps work with a mouse or a finger.
- Move (F) and Cancel (F again) are one button whose label changes with state. It is clear in the strip while a piece is carried.

## Touch usability

- Before: the strip's E cap was text, so a phone player had no visible place action (finding 4).
- After: a Place cap is a button. Its touch target is the same chip as the others (the panel uses `GameButton` chips; the UI standard's 14 `--u` minimum is not verified on the cap itself).
- The furniture panel still covers the room on the phone (finding 8) and overlaps the menu column (finding 12). Those are the largest touch problems left.

## Build flow (carpenter, from code)

- **Cost:** shown on each order row (`Carpenter_Cost`: coins, wood, stone); the order button is disabled when the player cannot pay. Not played.
- **Where:** the server refuses a building outside a farmable map (`NotPlaceable`) and past the limit per land (`LimitReached`). The ghost now shows both reasons before E (finding 2).
- **Upgrade:** not checked in this pass.
- **Undo:** none once built (finding 7). Fixed by the confirmation; an upgrade or demolish path would still need its own design.

## Mistakes that cannot be undone

| Action | Undoable? | Today | After |
| :-- | :-- | :-- | :-- |
| Place furniture (E) | Yes: stow (Q) or move (F) | Silent | Stow confirms (finding 5) |
| Stow furniture (Q) | Yes: place it again | Silent | Confirms |
| Place a farm building (E on a blueprint) | **No** | One press | Two presses on the same tile (finding 7) |
| Stow a chest with items | Refused (`NotEmpty`) | Message | Unchanged |

## Grades against `ui-standard.md` (§7 checklist)

| Check | 1280×720 | 390×844 | Note |
| :-- | :-- | :-- | :-- |
| 2 Nothing overlaps | Fail | Fail | Findings 8, 9, 11, 12 (the panel also covers the room, 8) |
| 3 Menu column one column, ≤ 8 | Pass | Fail | The column is not collapsed into "Menu" at 390×844 (finding 12) |
| 5 Panels use `GamePanel`, S/M/L width | Pass | Pass | Furniture panel is `GamePanel` |
| 6 Three font sizes, no clipping | Fail | Fail | "Stow" clipped, title overlapped (9, 10); a category chip hidden under the menu icons on the phone (12) |
| 7 No inner scrollbar at ≥ 1280×720 | Pass | n/a | |
| 8 Item icons whole and centred | Pass | Pass | |
| 10 Vietnamese and English fit | Partly | Pass | The notice fits in both. The Vietnamese stow line is clipped by the hotbar at 1280×720 (finding 11) |

## Follow-ups (not in this branch)

1. The ghost should not stay on a piece just placed (finding 3).
2. The furniture panel: collapsible on desktop, a sheet on the phone (finding 8).
3. Layout pass for the strip, the furniture title, the message line and the phone menu column (findings 9–12).
4. Q with nothing in front says why (finding 6).
5. Play the carpenter, chest and sprinkler flows through the same script (not done).
