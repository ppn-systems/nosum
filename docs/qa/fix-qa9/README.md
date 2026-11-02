# Fix QA 9: fixes for world QA pass 9

Fixes for the ranked issues in `docs/qa/world-pass9/README.md`, with before/after captures and the full world harness re-run.
Branch `claude/fix-qa9`. Architecture: ADR-108.

## Captures

- `before/<lang>/<size>/` is master's client, served on port 5081 against the new server (see caveat below). The files are
  `01-farm-start.png`, `02-after-water.png`, `03-inventory-open.png`.
- `after/<lang>/<size>/` is this branch's client, same steps. Sizes 1280x720, 1920x1080, 390x844; languages vi and en.
- Caveat: the server is this branch's. So "before" differs from master only in the client: its chain order and its hint placement.
  The before/after pair therefore shows the UI change, not the server's quest fix (which is server-side in both).

## Re-run of the world harness

`docs/qa/fix-qa9/world/<flow>/<flow>/<lang>/<size>/` (the harness writes `OUT/flow/lk/vk`, so the flow name appears twice).
Command: `NOSUM_QA_OUT=docs/qa/fix-qa9/world/<flow> node tests/browser/world-pass8.cjs <flow> vi,en <size>`, run for every flow at
every size on this branch's build (server Development, client `-p:NosumDevTools=true`).

Results at the time of writing are in the section "Harness results" below; the run was still going on mine, coop, bin and market.

## Issues

| # | Pass 9 issue | Status | What changed | Evidence |
|--:|:--|:--|:--|:--|
| HIGH | New player cannot leave the farm / reach town | Fixed (root cause found) | The tutorial chain put "Buy seeds" (the only step that sends the player to Town) after a harvest and a shipment. Now it follows "Water the soil". Watering a bare tilled tile also completes "Water the soil" (before, only a planted crop did). The bus stop itself works: walking east from spawn reaches it, and it reaches Town. | `FarmWireTests.Quests` (bare-tile watering), `QuestChainTests` (order); a browser probe walked farm to bus stop to Town (not committed); `after/en/1280x720/02-after-water.png` shows "Buy seeds" |
| 2 | English 390 top-right row overflow | Fixed | Row one is `nowrap` on a phone; the calendar drops its season name and tomorrow's forecast there. | `tests/browser/fix-qa9-check.cjs` (fails on master, passes here); `after/en/390x844/01-farm-start.png` |
| 3 | Hint card floating over the world | Fixed | The first-run hint is a toast-framed line in the bottom toast stack, above the hotbar. | `fix-qa9-check.cjs` ("hint sits in the bottom toast line", 390, 1280 vi/en); `after/*/02-after-water.png` |
| 4 | Co-op board hides the menu column at 1920 | Partly fixed | The menu column stays beside a window from 1600 px (the widest window is centred clear of it). The minimap is still hidden by its view model while a window is open; the hotbar is hidden by design when a panel is open. | `fix-qa9-check.cjs` ("menu column stays beside an open window") and `after/en/1920x1080/03-inventory-open.png` (inventory, not the co-op board) |
| 5 | Emote heart under the 8th column button at 1920 | Open | | |
| 6 | Wardrobe worn list cut off; "In hand" cut off at 1920 | Open | | |
| 7 | Buy-confirm dialog covers the market filter row | Open | | |
| 8 | Save-account dialog has no close button | Fixed (not browser-verified) | `AccountForm` passes `CloseText`/`OnClose` (cancels the upgrade). | Code only; no capture yet |
| 9 | Phone quest title loses its first letter under the scroll icon | Fixed | The scroll icon is `flex: none`. | `fix-qa9-check.cjs` ("quest title starts after the scroll icon", gap 4 px) |
| 10 | Furnace copy and feedback | Open | | |
| 11 | Mine floor 2 not reached in 3 of 6 runs | Reproduced, cause not fixed | In this re-run, en 1280 passed all four steps; vi 1920 failed the descent (17 far stones tried), and the farmer was on the forest surface afterwards. The tunnel walks over floor exits, which leave the mine. Not verified as the cause. | `world/mine/mine/vi/1920x1080/` (frames 02, 03) |
| 12 | Phone market seller name cut mid-word | Open | | |
| 13 | Minimap right edge 10 px inside the column at 1920 | Open | | |
| 14 | Coin badge and toast disagree after a sale | Open (not investigated) | | |
| 15 | Copy and wrapping (orphans) | Open | | |
| 16 | No feedback about rain | Open | | |
| 17 | Withered crop gives no cue | Open | | |
| 18 | Hotbar shows tool icons only | Open | | |
| 19 | Open panels hide the HUD (observation) | Open | | |
| 20 | Social harness (pass 9's own fix) | Unchanged | | |

## Checks

- Unit tests: 3748 passed (`tests/Nosum.UnitTests`).
- Integration tests: 846 passed locally (`tests/Nosum.IntegrationTests`); CI's integration step passed on this PR.
- `tests/browser/fix-qa9-check.cjs`: all six checks pass on this build; the phone top-row check fails on master.
- Build: 0 warnings, 0 errors (server, wasm, tests).
