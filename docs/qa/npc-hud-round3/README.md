# NPC and HUD round 3: browser check of ADR-093, before and after

Companion to [ADR-095](../../adr/ADR-095-waypoint-arrow-off-the-hud-and-the-round-three-check.md) and [`docs/design/critique-npc-hud.md`](../../design/critique-npc-hud.md).
ADR-093 (PR #301) shipped without captures. This folder is the browser check of its five items, run on the shipped build (`before/`, master at 72b11f9b)
and on this branch (`after/`), in Vietnamese and English, at 1280x720, 1920x1080 and 390x844 (an iPhone 14 profile).

- Harness: [`tests/browser/npc-hud-round3-capture.cjs`](../../../tests/browser/npc-hud-round3-capture.cjs). Development host on PostgreSQL (`nosum_dev`), client published with
  `-p:NosumDevTools=true`, served as static files, Chromium through Playwright. A guest is signed in; its quest stage is set in the database (the tester's shortcut,
  as the acceptance runs do), and the guest signs in again after each change, because quests are read at sign-in.
- Files: `<name>-<lang>-<WxH>.png` and `round3-<WxH>.json` (the metrics below, read from the canvas pixels and the DOM rectangles of the HUD).
- Measurements are the canvas pixels, not a judgement from the picture: the arrow is 333 gold pixels at scale 3 (37 pixels a glyph, 9 by 9 cells); the box is read
  from those pixels and compared with every element under `.game-hud`.
- Not measured: the phone has no minimap card, so the minimap dot is checked at the two desktop sizes only. `.emote-layer` is a full-viewport transparent layer, so the
  overlap check reports it on every capture; it draws nothing and is listed in the metrics for transparency.

## Grades

Scale: **pass** (verified in the browser and matches the ADR), **partial** (verified in part; the rest is named), **fail** (verified and wrong, fixed here or open).

| # | Item (ADR-093) | Before (master) | After (this branch) | Grade |
| :-- | :-- | :-- | :-- | :-- |
| 1 | Edge arrow for an off-screen quest target, never over a HUD region | **Fail.** Arrow under the energy gauge and hotbar (Town, 1280 and 1920); under the calendar bar (Forest, 1280 and 1920). Arrow drawn in the open at 390 and in the Mine. | Arrow slides along its edge until it is clear. No overlap with any HUD element at 1280, 1920 or 390 (only the transparent `.emote-layer`). Forest arrow at 1920 is visible above the calendar, pointing at the tree. | **Pass** (after the renderer fix below) |
| 2 | Minimap dot shows | Pass at 1280 and 1920 (36 and 100 green pixels in the minimap card on Town, Forest and Mine). No minimap at 390. | Same. | **Pass** (desktop); phone: no minimap card to show a dot on |
| 3 | Merchant quest greeting, vi and en | Pass at all three sizes (text read back equals the quest line in both languages). | Same. | **Pass** |
| 4 | Name plate lift clears the exit's direction label | Clear at 1280 vi (`name-lift-farm-door-vi-1280x720.png`) and at 390 (`...-390x844.png`): the plate sits above "↑ Nhà nông trại" with a gap. | Unchanged: the 16 world-pixel lift is 32, 48 and 80 screen pixels at scales 2, 3 and 5, against a label about 18 screen pixels tall. The lift was not replaced by a measured box (ADR-095 D4). | **Pass** at the scales the game uses. Open: scale 1 is not reachable in the game, so the band is not measured there |
| 5 | Hints wait for 2 s of idle | Hint 2.34 s (1280), 2.40 s (1920), 2.32 s (390) after the last key. | 2.33 s, 2.38 s and 2.31 s. Input held for 3 s keeps the hint off the screen. | **Pass** (Vietnamese). English: no second hint came up in the run (the step's hint was already seen on this account), so the English wait is covered by the unit test only |
| 6 | Forest and Mine waypoints at the nodes, not the spawn | Waypoints on the spawn tiles (68, 0) and (23, 10). | Forest: the tree at (65, 9), the nearest to the arrival. Mine: the cave shaft at (23, 9). The arrow points at them from the far side of the map (Forest up, Mine left). | **Pass** |
| 7 | Zero allocations per frame | Held-key allocation 22.3 KB/s (1280), 48.6 KB/s (1920), 25.3 KB/s (390). | Held: 21.5 KB/s (1280), 21.6 KB/s (1920), 21.6 KB/s (390). Idle: 18.2 KB/s, 18.3 KB/s, 17.4 KB/s. | **Partial.** The hint no longer creates a timer per frame; the held figure at 1920 fell from 48.6 to 21.6 KB/s, but this run does not isolate the cause. The frame still allocates when idle, so the rule is not met; the source is not isolated in this round (ADR-095 consequences) |

Interop: no new crossing. The arrow is two draw commands in the frame's one submit; the HUD rectangles are read at most every 250 ms, as before.

## What the captures found and what changed

1. **The arrow sat under the HUD** (item 1). The arrow is canvas pixels, the HUD is DOM over it, and the engine does not know the HUD. The renderer already reads the HUD
   rectangles for world labels, so the arrow now slides along its edge in the renderer until it is clear (`renderer.js`, `fitArrow`).
2. **The HUD filter dropped the top-right column at 1920x1080.** `refreshHud` skipped any region at least 90% of the canvas *height*, and the column is taller than
   that at 1080. Labels and the arrow both ignored it. The filter now skips only regions covering most of both dimensions.
3. **The test clock did not simulate timestamps**, so the hint's idle count read real time and never fired in the unit tests. The test clock now reports the same
   simulated time as its timers (`SettingsTests`).
4. **The hint re-armed a timer on every frame of input** (`NoteActivity` called `ArmIdle` per frame). It now records the time; the timer re-arms for the rest of the
   quiet time (`OnboardingViewModel`).
5. **The Forest and Mine waypoints pointed at spawn tiles** (item 6).

## Open

- The frame allocates about 17 KB/s when idle (item 7). Not isolated here.
- The bus stop's direction label ("→ Trạm xe buýt") is drawn over the farmer's head beside the farmhouse door (critique finding 5). It is a world label, not a name plate,
  and the lift does not cover it.
- The English hint wait is not in the browser run (item 5).
- No ore tile exists to point the Mine waypoint at: the copper lies on floors generated per player, so the cave shaft is the fixed node.

## Files

- `before/`: `arrow-town`, `greeting-seed-keeper`, `arrow-forest`, `arrow-mine`, `name-lift-farm-door` for vi and en at the three sizes (33 files with the metrics).
- `after/`: the same set from this branch.
