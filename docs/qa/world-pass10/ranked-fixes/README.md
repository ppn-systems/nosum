# World QA pass 10: fixes to the ranked issues

Works through the ranked issues of [`docs/qa/world-pass10/README.md`](../README.md) on `origin/master` 46d3fb6 (after #361). The layout
decisions are in [ADR-116](../../../adr/ADR-116-window-layout-rules-from-world-pass-10.md). Issue 4 is left for an owner decision, as asked.

Frames: `before/` is `origin/master` (46d3fb6, built and run on its own port); `after/` is this change. Both were taken with the same harness
(`tests/browser/world-new-player.cjs`) and the same window probe, at 1280x720, 1920x1080 and 390x844, in Vietnamese (vi) and English (en).

## Status per ranked issue

| # | Issue | Status | What changed | Evidence |
|--:|:--|:--|:--|:--|
| 1 | A new player cannot leave the farm on day 1 | **Fixed in the harness; the product was right** | The bus stop is an edge exit (`Farm.tmx` lists `80 15..18 BusStop 11 23`; `WarpRule.TryEdge`): the farmer leaves by walking right off column 79 on rows 15 to 18. The old step pressed E at the farmhouse door, which tests nothing. The harness now teleports to (78,16), holds D, and checks that the farmer's tile x falls below 40 (the BusStop arrival is x 11). It passes at all six configurations, on master and on this change. | `newplayer/*/*/09-06-bus-stop-walk-east.png` |
| 2 | Phone (390) map entry point | **Harness fix; the product was right** | At 390 the map is a tile of the Menu window (Menu, then World map), as ui-standard section 6 says. The harness opened the map from the column button, which the phone does not have. It now taps the tile. | `newplayer/*/390x844/04-world-map.png` |
| 3 | Hint and notice cards float over the world | **Fixed** | The combo and level-up badge strip (14 u) moved out of the hotbar and above the toast line (`Progress/HotbarBadges`). The toasts and the first-run hint now sit directly on the hotbar's frame, with no empty band under them. | `newplayer/*/*/01-farm-start.png` |
| 4 | At 1280x720 a window hides the minimap, the column and the hotbar | **Not changed (owner decision)** | Left as it was. Section 2 of the standard does not say whether a desktop hide is intended. | `captures/*-1280x720-*.png` show it |
| 5 | Co-op window covers the minimap at 1920; hotbar missing | **Fixed** | From 1600 px an XL window starts below the minimap card (72 u). The hotbar is drawn under the co-op only, display-only; the taller windows still hide it. | `captures/coop-1920x1080-*.png` |
| 6 | Furniture window top-left, tab row clipped | **Fixed (one trade-off)** | The arrange panel is a centred M window in the centre region. Its category tabs wrap onto more rows instead of scrolling sideways. **Trade-off:** a centred window covers the middle of the room while the player arranges furniture; the old top-left dock kept the room open. The owner asked for centred, so it is centred; flagged in ADR-116. | `captures/furniture-*.png` |
| 7 | Inner clipping at 1920: vi profile body; "In hand" in the inventory | **Profile fixed; "In hand" open** | The profile window is L, so its Vietnamese text wraps to fewer lines and the sign-out row fits without an inner scroll. The "In hand" description is still under a fade at 1920: the inventory body is about 880 px and the window has about 895 px. Fixing it needs a layout choice (ADR-116 consequences). | `captures/profile-*.png`; "In hand" not re-captured |
| 8 | Sign-out confirm has no close control | **Not changed (not required)** | `ConfirmDialog` answers with its own buttons, and Cancel closes it. Section 3's close control is for windows, not for a two-answer question. | — |
| 9 | Orphan words and bad wraps | **Partly fixed** | Last two words joined with a no-break space in the `.resx` values: the carpenter's "buy another.", the co-op's "a bag slot to deposit." and "for tilling and building.", "One more row.", "Plant a new seed over it.", the backpack's "{0} ô." and "hàng ô.", the mail's "khi xóa.", and every "Thử lại nhé." (13 keys). The quest reward line was tried and reverted: `QuestTrackerViewModelTests` asserts its plain text. Other orphans in the pass-10 list are not in these strings and are not changed. | — |
| 10–16 | Furnace copy, emote heart at 390, touch copy, mine floors, wardrobe, phone details, coin tween | **Not in this change** | Not part of the ranked list this change was asked to fix; they stay open in the pass-10 README. | — |

## Harness changes

- `tests/browser/world-new-player.cjs`: the exit step walks off the farm's east edge (issue 1); the map step opens the map from the Menu
  window at 390 (issue 2); every window is closed before the walk.

## Verification

- `dotnet build wasm/Nosum.Wasm.slnx -p:NosumDevTools=true` on this change: 0 warnings, 0 errors (`-warnaserror` is the default).
- `node --test tests/browser/label-fit/*.mjs tests/browser/modules/*.mjs`: 40 of 40 pass.
- `dotnet test tests/Nosum.UnitTests`: see the PR description for the run's totals.
- `world-new-player.cjs` on the change: 6 of 6 runs, 0 failed steps, 0 page errors (vi and en at all three sizes).
- The same harness on `origin/master`: 6 of 6 runs pass too, which confirms the exit and the map were harness faults, not product faults.
- Each window capture was read by eye at the size it was taken.
