# World QA pass 10: the open items of pass 9

Continues `docs/qa/world-pass9/README.md`, items 9, 11, 21, 22 and 23, on `origin/master` at #349 (`391fa74`). Frames are in `fixes/after/`
(1280x720, 1920x1080, 390x844; vi and en where taken). The checks are `tests/browser/world-pass10-fix-check.cjs` (20 of 20 ok) and the
capture script `tests/browser/world-pass10-capture.cjs`.

## Status per item

| # | Item | Status | Change and evidence |
|--:|:--|:--|:--|
| 9 | Phone quest title, first letter under the scroll icon (en 390) | Fixed (verified) | #349's `flex: none` on the scroll icon keeps the icon from covering the first glyph. Zoomed at 390 en for the five longest titles: the first glyph is clear of the icon in each (`fixes/after/quest-title-en-390x844.png`). Regression check in the fix check. |
| 11 | Mine harness: floor 2 not reached, en 390 aborted, fight ok while the farmer was sent home | Partly fixed (harness) | See "Mine harness" below. Intermittent; not yet 100%. |
| 21 | Trade and co-op grids clip a fourth row; friends list clips at the bottom on desktop | Fixed for the cases measured | Trade is XL: offers left, the 9-column bag right, 16 u slots (as the chest). Co-op warehouse is XL: stock left, bag right. Friends is L (one line per friend). Frames at 1280, 1920 and 390, vi and en. |
| 22 | Chat card under the trade panel at 1280; menu column covers the emote at 1920 | Fixed | The chat line steps aside while a centre window is open (the quest card already did). The emote cell moves left of the column at every desktop height: the old rule needed 880 px of height, and 840 to 879 px overlapped (`1920x840`, `1920x857`, `1680x840`, `1536x864`). Measured with the sweep in the fix check. |
| 23 | Menu label "Profile" wraps, leaving "N" alone | Fixed | The hotkey is a small corner badge on its tile; the name is one line (as every tile in the menu). |

## Mine harness (item 11)

The trace (`DEBUG_TUNNEL=1`) showed three causes, all in the harness, not in the game:

1. The tunnel's fallback keys walked the farmer off the floor's west edge (x about 2), back onto the surface, while the arrival tile is six tiles
   from that edge. Fixed: a step onto x or y of 3 or less is skipped unless the goal or a node is there; a walk off the floor is detected and the
   descent re-enters floor 1 (at most three times).
2. The fight step recorded `ok` without checking anything. Fixed: it fails when the farmer is not on floor 2 after the swings.
3. The fight came last, after about 40 tunnel actions on floor 2 where monsters hit the farmer; a knock-out carried the farmer home. The fight now
   runs straight after the descent, before the ore.

Runs after the fix: en 390 passed all four steps (24 candidates); vi 1920 passed once and failed once (the walk-out). A walk-out can still happen
on some seeds, so the flow is not yet reliable. Not fixed here: a knock-out during the descent (no QA call restores HP, and the hotbar is full
so a potion cannot be used).

## Pre-existing failure found while capturing (not in this PR)

The social harness step `coop-log` fails at all three sizes and both languages, on master before this PR too
(a baseline run before this PR, and again after it). The co-op window is not open when the step looks for the Co-op text. This is a harness
issue to take up separately.

## Reproduce

```
DOTNET_ENVIRONMENT=Development dotnet run --no-build --project server/Nosum.Host
dotnet run --no-build --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080
node tests/browser/world-pass10-fix-check.cjs
node tests/browser/world-pass10-capture.cjs en 390x844
BASE=http://localhost:5080/ LANGS=vi,en SIZE=1920x1080 OUT_DIR=docs/qa/world-pass10/social/1920x1080 node tests/browser/social-round3-play.cjs
```
