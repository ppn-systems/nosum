# Placement, round 4 (QA)

Round 4 of the placement critique ([`docs/design/critique-placement.md`](../../design/critique-placement.md)): finding 16 (the feet rule: the ghost
steps to the nearest tile that fits clear of the feet, ADR-103) and finding 17 (a craft names what it made) are fixed. The phone and desktop frames
are at 1280×720 and 390×844, each in Vietnamese and English, before (`origin/master` at 0e37d76) and after (this branch).

## Frames

| Folder | What | Made by |
| :-- | :-- | :-- |
| `before/{desktop,phone}-{vi,en}/` | `origin/master` (0e37d76, the round-3 merge) | `PLACEMENT_QA_ROOT=docs/qa/placement-round4 node tests/browser/placement-round4-capture.cjs before` |
| `after/{desktop,phone}-{vi,en}/` | this branch | `… placement-round4-capture.cjs after` |

Frame names are the step order of the script. `notes.json` holds the strip text at each step and the recipe survey. `errors.txt` lists console
and page errors (none in any of the eight runs).

Both sets ran against a dev server (`DOTNET_ENVIRONMENT=Development`, PostgreSQL on localhost) and a dev client (`-p:NosumDevTools=true`, `?qa=1`).
The "before" set ran on a worktree of `origin/master` with its own server and client, after the "after" set; the two were not run side by side.

## What was played

1. **Coop purchase at the carpenter (real).** Coins and materials granted through the QA hook; the blueprint bought at the counter.
2. **Coop placement.** The blueprint held, the first E asks ("A finished building cannot be moved or stowed. Press E again to build here."), the second
   E builds (`42-second-press-built.png`).
3. **Chest (real craft).** Crafted for 50 wood at the crafting page. The page's status line names it: "Crafted Chest x1. It is in your bag." /
   "Đã chế tạo Rương x1. Đã vào túi của bạn." (`after/*/22-chest-crafted.png`). The chest is then placed (`24-chest-placed.png`) and opened (`25-chest-open.png`).
4. **Furnace and sprinkler (granted).** Granted through the QA hook (their recipes need Mining 1 and Farming 2), held, walked to and placed
   (`330`/`331` furnace, `340`/`341` sprinkler). The frames show both pieces placed; no refusal is visible in them.

## Findings and status

| # | Sev | Finding | Status |
| :-- | :-- | :-- | :-- |
| 16 | M (phone) | "You are standing there." on a tile that looks free (the feet box reaches into the aimed tile). | **Fixed (ADR-103).** The rule is unchanged (server and client); the ghost steps to the nearest clear tile beside the aim and E places it there. Unit-tested (`PlacedSetTests.TryFindClearAnchor_*`). **Not reproduced in the browser this round**: the walk did not stand the farmer under the aimed tile in any of the eight runs (`notes.json` `furnaceGhost` is empty in all, and round 3 hit it in 3 of 6 and 4 of 6 runs). The before and after frames therefore do not show the difference; the unit tests are the evidence. |
| 17 | L | Crafting gives no confirmation. | **Fixed (ADR-103).** The crafting page's status line (and the message line, when the page is closed at once) names the item and the total made. Vietnamese and English. Unit-tested (`CraftingViewModelTests.Craft_Ok_*`, `Craft_Refused_*`). Frames: `after/desktop-en/22-chest-crafted.png`, `after/phone-vi/22-chest-crafted.png`. |

## Gaps left (logged)

- **Finding 16 is not reproduced in the browser.** The capture walks the farmer and presses E; it does not place the farmer at the feet-in-the-way spot
  on purpose. A deterministic check needs a position under the aimed tile (a QA teleport to a pixel position, or a sub-tile QA hook). Not built this round.
- **The strip text is empty in every run for the placement steps** (`furnaceGhost`, `chestPlaced`, `chestOpen`, …). The strip is hidden while the
  furniture or chest panel is open, so the notes read nothing there. The frames are the record for those steps.
- **The step-aside ghost is not labelled** as moved. The ghost is green on the tile beside the aim; a copy line for the move is an open design call (ADR-103 consequences).
- **Furnace and sprinkler crafting** is still not played (skill levels; the QA hook raises no skill).
- **The Vietnamese craft line at 390** fits on one line in the status row (`after/phone-vi/22-chest-crafted.png`); at 1280 it is one line too.

## Tests

- `dotnet test tests/Nosum.UnitTests`: **3649 passed, 0 failed** (the round-3 count was 3593; the new tests are `PlacedSetTests` (three step-aside
  cases) and `CraftingViewModelTests` (two craft confirmation cases)).
- `dotnet test tests/Nosum.IntegrationTests`: **828 passed, 0 failed** on the rerun. The first full run (`tests/Nosum.Tests.slnx`) had one failure while the browser captures were running on the same machine; the failing test was not identified, and the rerun on an idle machine passed.
- Builds (`--no-incremental`) of `wasm/Nosum.Wasm.slnx`, `gameplay/Nosum.Gameplay.slnx`, `server/Nosum.slnx`, `engine/Nosum.Engine.slnx` and `tests/Nosum.Tests.slnx`: **0 warnings, 0 errors.**
