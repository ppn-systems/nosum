# Placement, round 2 (QA)

Round 2 of the placement critique ([`docs/design/critique-placement.md`](../../design/critique-placement.md)): findings 3, 6 and 8–12 are fixed here, and the carpenter build flow (finding 7) is played in the browser.

## Frames

| Folder | What | Made by |
| :-- | :-- | :-- |
| `before/{desktop,wide,phone}-{vi,en}/` | `origin/master` (4221f3b, PR #287) at 1280×720, 1920×1080 and 390×844 | `PLACEMENT_QA_ROOT=docs/qa/placement-round2 node tests/browser/placement-capture.cjs before` |
| `after/{desktop,wide,phone}-{vi,en}/` | this branch | `TAP_PLACE=1 … placement-capture.cjs after` |
| `after/build-{desktop,wide,phone}-{vi,en}/` | the carpenter build flow: the blueprint held, the ghost, the first E (confirmation), the second E (built), Q with nothing in front; `notes.json` holds the strip text at each step | `… carpenter-build-capture.cjs after` |

Frame names are the step order of the scripts. `errors.txt` in each folder lists console and page errors (none in the after set).

The carpenter flow has no `before` set: master already had the confirmation (PR #287), and round 2 changed nothing in that path. The counter purchase is not played: the blueprint (item 610) is granted through the QA hook (`window.nosumQa.grant`), then placed on the farm.

## Layout check

`tests/browser/placement-layout-check.cjs` measures the furniture panel, the top-right column, the toast line and the hotbar with the panel open in the farm, and prints every overlap. It also checks that the panel does not cover the middle of the viewport and that the panel does not scroll at 1280×720.

| Viewport | vi | en |
| :-- | :-- | :-- |
| 1280×720 | PASS (panel at 24,24, 372×538) | PASS (same) |
| 1920×1080 | PASS (panel at 40,40, 620×810) | PASS (same) |
| 390×844 | PASS (panel at 16,104, 358×291) | PASS (same) |

## Grades against `ui-standard.md` section 7

Before is `master` (4221f3b); after is this branch. "Partly" means something is still open, and the note says what.

| # | Check | Before 1280 | Before 390 | After 1280 | After 1920 | After 390 | Note |
| :-- | :-- | :-- | :-- | :-- | :-- | :-- | :-- |
| 1 | In its region; no floating card over the world | Fail | Fail | Pass | Pass | Pass | The panel is in the top-left region, not a centre window over the room |
| 2 | Nothing overlaps | Fail | Fail | Pass | Pass | Pass | Layout check: no overlapping pair at any size |
| 3 | Menu column: one column, ≤ 8 | Pass | Fail | Pass | Pass | Partly | Column and minimap button are hidden while the panel is open; the general phone collapse (§6) is still open |
| 4 | Edges line up | Pass | Pass | Pass | Pass | Pass | One card in the region (the quest card is hidden while arranging) |
| 5 | `GamePanel`, S/M/L/XL width | Pass | Pass | Partly | Partly | Partly | Shared frame and title bar; the width is the region's 124 u, not a named size (follow-up 2) |
| 6 | Three font sizes, nothing clipped | Fail | Fail | Pass | Pass | Pass | The title is whole, the key caps wrap inside the panel, the toasts are whole |
| 7 | No inner scrollbar at ≥ 1280×720 | Pass | n/a | Pass | Pass | n/a | `scrolls` is false in the layout check |
| 8 | Item icons whole and centred | Pass | Pass | Pass | Pass | Pass | Unchanged |
| 9 | Spacing in `--u` and gap tokens | Pass | Pass | Pass | Pass | Pass | Phone panel cap uses `50vh - 26 u` (a size, not a spacing) |
| 10 | Vietnamese and English fit | Partly | Pass | Pass | Pass | Pass | The Vietnamese stow line was cut by the hotbar before; it is whole now |

## Findings

- **3 (ghost on the placed piece): fixed.** `after/desktop-en/03-after-place.png`: no ghost on the chair just placed.
- **6 (Q with nothing in front): fixed.** `after/desktop-en/06-after-stow.png`: "Nothing in front of you to stow." on the strip for 2.5 s.
- **8 (panel covers the room): fixed.** The room's centre is open at every size (`after/desktop-en/02-ghost-open-floor.png`, `after/wide-vi/02-ghost-open-floor.png`, `after/phone-en/02-ghost-open-floor.png`).
- **9 ("Furniture" title): fixed.** The title has its own header row; the after frames show nothing over it. I could not reproduce the overlap in the before frames at 1280×720, so this is verified by the after frames, not by a before-and-after difference.
- **10 ("Stow" cut off): fixed.** The key caps wrap inside the panel (`after/desktop-en/02-ghost-open-floor.png`).
- **11 (stow line under the hotbar): fixed.** The panel no longer pushes the top row down, which had squeezed the toast line into the hotbar. `after/desktop-vi/08-stow-confirmation.png`.
- **12 (phone menu overlap): fixed while arranging.** `after/phone-en/02-ghost-open-floor.png`.
- **7 (carpenter, building confirmation): played.** `after/build-desktop-en/`: the first E shows "A finished building cannot be moved or stowed. Press E again to build here."; the second E builds the coop (`03-second-press-built.png`, the blueprint slot empties).

## Tests

- `dotnet test tests/Nosum.UnitTests` (Nosum.UnitTests.csproj), after merging master be97af9: 3509 passed, 0 failed. New: `SettledGhostTests` (4), two `PlacementViewModelTests` (the stow note in vi and en; the note's change notifications).
- `dotnet build wasm/Nosum.Wasm.slnx`: 0 warnings, 0 errors. `dotnet build engine/Nosum.Engine.slnx`: 0 warnings.
- The browser scripts ran against the dev client (`-p:NosumDevTools=true`) and the dev host (`DOTNET_ENVIRONMENT=Development`, PostgreSQL on localhost).

## Open

- At 1280×720 in Vietnamese with the three-line build confirmation and an empty list, the panel's body scrolls and the Place and Pick up buttons sit below the visible part (`build-desktop-vi/02-first-press-confirm.png`). The layout needs a decision (one scrollable tab row, or a larger width); the scroll is the fallback.
- The general phone menu collapse (§6) and the panel width (§3) need a decision of their own.
- The farm hint shows in the farmhouse while arranging (finding 13).
- Chests, furnaces and sprinklers are not played through the browser flow.
