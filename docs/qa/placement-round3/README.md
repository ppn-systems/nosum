# Placement, round 3 (QA)

Round 3 of the placement critique ([`docs/design/critique-placement.md`](../../design/critique-placement.md)): finding 13 (the farm hint in the farmhouse) is fixed; the phone menu collapse (ui-standard section 6) is checked and already in `master`; the chest, furnace, sprinkler and coop flows are played in the browser, the coop through the carpenter's counter. Two new placement findings (14 and 15) are fixed, one (16) is copy-fixed with its rule still open, and one (17) is recorded.

## Frames

| Folder | What | Made by |
| :-- | :-- | :-- |
| `before/{desktop,wide,phone}-{vi,en}/` | `origin/master` (72b11f9) at 1280×720, 1920×1080 and 390×844 | `PLACEMENT_QA_ROOT=docs/qa/placement-round3 node tests/browser/placement-round3-capture.cjs before` |
| `after/{desktop,wide,phone}-{vi,en}/` | this branch | `… placement-round3-capture.cjs after` |
| `before/hint-{desktop,wide,phone}-{vi,en}/`, `after/hint-…/` | the farmhouse hint: `01-farm-hint.png` on the farm, `02-farmhouse-hint.png` indoors; `hint.json` holds the hint text | `… placement-round3-hint-capture.cjs before\|after` |

Frame names are the step order of the script. `notes.json` holds the strip text at each step and the recipe survey (every recipe's name, what it makes and its unlock line). `errors.txt` lists console and page errors (none in either set).

Both sets use the same scripts. The "before" set was taken against a worktree of `origin/master` with its own client and server; the "after" set against this branch.

## What was played

1. **Coop purchase at the carpenter (real).** Coins, wood and stone are granted through the QA hook (`window.nosumQa.grant`; a stack holds 99, so wood comes in four grants). The blueprint is bought at the counter: E, Build, Buildings, Buy blueprint (`*/desktop-en/13-blueprint-bought.png`). Not granted.
2. **Coop placement.** The blueprint is moved from the bag into hotbar slot 9 in the inventory, held, the first E asks, the second E builds (`after/desktop-en/42-second-press-built.png`).
3. **Chest (real craft).** The crafting page is read first (every recipe, `notes.json`), then Chest is crafted for 50 wood (`after/desktop-en/22-chest-crafted.png`: wood 96 → 46), moved to the hotbar, placed (`24-chest-placed.png`) and opened with E (`25-chest-open.png`).
4. **Furnace and sprinkler (granted).** Their recipes are locked to a skill: the furnace needs Mining 1 (100 XP) and the sprinkler Farming 2 (380 XP), both earned by play (`notes.json`). The finished piece is granted through the QA hook and its placement is played (`330`/`331` furnace, `340`/`341` sprinkler). The walk-then-press furnace placement was refused by the feet rule (finding 16) in 4 of 6 branch runs (3 of 6 master runs); the sprinkler is then placed clear of it (`after/desktop-en/341-sprinkler-placed.png`), which returns the furnace to the bag.
5. **Farmhouse hint.** Walk from the farm to the door, enter, close the arrange panel with Escape, wait for the idle moment (`hint-*/02-farmhouse-hint.png`).

Not played: crafting a furnace or a sprinkler (the skill is not reached in one session); the QA hook raises no skill (`DevLevelUp` is a banner only).

## Findings and status

Severity as in the critique: **H** a player is blocked or loses something; **M** a player is confused or slowed; **L** cosmetic.

| # | Sev | Finding | Frames (before → after) | Status |
| :-- | :-- | :-- | :-- | :-- |
| 13 | L | **The farm hint shows in the farmhouse.** The first-run tip "Select the hoe and press the use key to till a patch of soil. Reward: 10 coins." shows indoors, after the arrange panel is put away. | `before/hint-desktop-en/02-farmhouse-hint.png` (hint.json: "Select the hoe…") → `after/hint-desktop-en/02-farmhouse-hint.png` (hint.json: "Go to the seed shop in Town and buy seeds. Reward: 20 coins.") | **Fixed.** `OnboardingViewModel.OnFarm`, set on every map join (`ClientWiring`): the Use step and the four farm quest tips (`till_soil`, `water_crop`, `first_harvest`, `ship_produce`) are skipped off the farm, and the next tip that applies shows. Unit-tested (`SettingsTests`). |
| §6 | M | **The phone menu column and minimap collapse (ui-standard section 6).** Round 2 listed the general collapse as open. | `before/phone-en/24-chest-placed.png`: no panel open, the column is one Menu button and the minimap is one map button | **Already in `master`** (PR #276 and `GameHud.razor.css`, `page-guard.js`, `ToolbarViewModel.ColumnCollapsed`, `MinimapViewModel.Hidden` on a narrow screen). No code change; the round-2 note was stale. Confirmed by the frames above. |
| 14 | M | **The arrange panel said "You have no furniture yet" for a chest, a furnace or a sprinkler in hand.** The panel opens for them but lists only furniture. | `before/desktop-en/330-furnace-held.png` → `after/desktop-en/340-sprinkler-held.png` (the sprinkler is listed under All, in the Equipment group) | **Fixed.** The three are listed: the chest in Storage, the furnace and the sprinkler in a new `Equipment` category (`FurnitureCategory.Equipment`, "Equipment" / "Thiết bị"). Unit-tested (`FurnitureCatalogViewModelTests`). |
| 15 | M | **E opens a chest or a furnace in front instead of placing the piece in hand.** A sprinkler held next to a furnace would open the furnace. | Seen in an intermediate capture pass, not kept as a frame; the final walk does not stand next to a furnace. | **Fixed.** While a placeable is in the selected hotbar slot, the action key goes to placement (`PlacementActions.HoldsPlaceable`, `MapScene`); the refusal ("There is furniture there already.") is shown instead. Not re-captured before/after. |
| 16 | M (phone) | **"You are standing there." on a tile that looks free.** The farmer's feet box (10 px) reaches into the target tile when the farmer stops in the tile's right 5 px. The walk-then-press furnace placement hit it in 3 of 6 master runs and 4 of 6 branch runs (`furnaceGhost` in each `notes.json`). | `before/desktop-vi/` (ghost "Bạn đang đứng ở chỗ này.") and `after/desktop-en/331-furnace-held.png` (ghost "You are standing there. Step aside to place it.") | **Copy fixed:** "Step aside to place it." / "Hãy bước sang một bên để đặt.". Stepping back one tile in the script did not clear it. **Open:** the rule itself (`PlacedSet.CoversFeet`, shared with the server) is unchanged; the fix belongs with a targeting change (the target tile from the feet's centre) and its own check. |
| 17 | L | **Crafting gives no confirmation.** The wood count drops and the chest goes to the bag without a line. | `after/desktop-en/22-chest-crafted.png` (only the count changes) | **Open** (crafting page, not placement). |
| 18 | — | **Furnace and sprinkler are not reachable in a short session.** Their recipes need Mining 1 and Farming 2. | `notes.json` → `recipes` (`Requires Mining level 1`, `Requires Farming level 2`) | Recorded. The pieces are granted through the QA hook in this pass, and the placement is what is played. |

## Grades against `ui-standard.md` section 7

"After" is this branch, from the frames above. "Before" is `master`, where it differs.

| # | Check | After 1280 | After 1920 | After 390 | Note |
| :-- | :-- | :-- | :-- | :-- | :-- |
| 1 | In its region; no floating card over the world | Pass | Pass | Pass | The hint and the strip are in the bottom centre and the arrange panel in the top-left region. |
| 2 | Nothing overlaps | Pass | Pass | Pass | The arrange panel covers the room's upper part on the phone (as round 2 accepted, `placement-round2` 8); nothing overlaps another region. |
| 3 | Menu column: one column, ≤ 8, same size | Pass | Pass | Pass | 1920: the column is 8 cells with the Menu button last (`after/wide-en/42-second-press-built.png`). 390: one Menu button and one map button (`before/phone-en/24-chest-placed.png`, same in after). |
| 4 | Edges line up | Pass | Pass | Pass | Unchanged. |
| 5 | `GamePanel`, S/M/L/XL | Pass | Pass | Pass | The chest is XL (`after/desktop-en/25-chest-open.png`); the furniture panel is M and lists the Equipment group under the existing chips. |
| 6 | Three font sizes, nothing clipped | Pass | Pass | Pass | The phone strip line (vi, the longest) is one line in the strip (`after/phone-vi/331-furnace-placed.png`). |
| 7 | No inner scrollbar at ≥ 1280×720 | Pass | Pass | n/a | The category chips scroll sideways (round 2); at 1280 the Equipment chip is past the visible row, the All tab lists the pieces. |
| 8 | Item icons whole and centred | Pass | Pass | Pass | The furnace and sprinkler icons in the panel are whole (`after/desktop-en/340-sprinkler-held.png`). |
| 9 | Spacing in `--u` and gap tokens | Pass | Pass | Pass | Unchanged. |
| 10 | Vietnamese and English fit | Pass | Partly | Pass | vi checked at 390 (the step-aside line) and at 1280 (the minimap tip and the hint). vi at 1920 was not inspected frame by frame. |

## Tests

- `dotnet test tests/Nosum.UnitTests`: **3593 passed, 0 failed.**
- `dotnet build` of `wasm/Nosum.Wasm.slnx`, `gameplay/Nosum.Gameplay.slnx`, `server/Nosum.slnx` and `engine/Nosum.Engine.slnx`: **0 warnings, 0 errors.**
- New unit tests: `SettingsTests` (farm tip off the farm and back; the farm quest tip not shown indoors), `FurnitureCatalogViewModelTests` (the crafted pieces are listed; the Equipment tab lists the furnace and the sprinkler only). The tab count test is now 10 (All and nine categories).
- The browser scripts ran against the dev client (`-p:NosumDevTools=true`) and the dev host (`DOTNET_ENVIRONMENT=Development`, PostgreSQL on localhost). `tests/browser/placement-round3-capture.cjs` and `tests/browser/placement-round3-hint-capture.cjs`.

## Open

- Finding 16: the feet-in-the-way rule (targeting or `CoversFeet`) is unchanged; the phone furnace placement needs a step aside.
- Finding 17: a craft confirmation line.
- Furnace and sprinkler crafting is not played (skill levels).
- The Equipment chip sits past the chip row at 1280 (the row scrolls); a shorter label or a second row is a design call.
