# World QA pass 9 (branch `claude/qa-pass9`, base `origin/master` 42c67c4)

Pass 8 drove each feature; pass 9 runs the same harness on current master at 1280x720, 1920x1080 and 390x844, in Vietnamese and English,
grades every frame by eye against the 10-point checklist of `docs/design/ui-standard.md` section 7, and plays one new player's first day
as a guest. The server ran in Development (`DOTNET_ENVIRONMENT=Development`) and the client was the tester build
(`-p:NosumDevTools=true`), so the harness could use the QA calls (ADR-079). The matrix ran one flow at a time, because the dev clock is shared.

## Result

Each row is one flow over the six combinations (3 sizes x vi/en). "Frames" are the PNGs graded by eye. "Steps" are the harness's own
checks (DOM text, counts, the server's reply); a step's `ok` means the check passed, not that the frame was reviewed.

| Flow | Steps per run | Harness result (6 runs) | Frames graded by eye | Points failed in the frames |
|:--|:--|:--|--:|:--|
| farm (animals, pets) | 15 | 6 of 6 ok, 0 page errors | 78 | 1 (1920 minimap edge), 2 (emote heart under the 8th column button at 1920; phone top row), 4 (1920 minimap edge), 6 (en 390 quest title clipped; orphan words), 1 and 5 ("Placed." floats over the coop wall, 1280 vi and 1920) |
| town (tailor, wardrobe, world map) | 8 | 6 of 6 ok | 42 | 6 and 7 (wardrobe worn list cut off at desktop sizes), 6 (inventory "In hand" cut off at 1920), 2 (phone bag panel under the heart), 1 and 2 (en 390 top row: settings gear wraps or hides), 4 (1920 minimap edge) |
| account (profile, save link, sign out) | 3 | 6 of 6 ok | 18 | 5 (save-account dialog has no close button, all six), 7 (1920 vi profile fade under sign-out, probable) |
| mine (elevator, floors, ore, fight) | 4 | 3 of 6 with failed steps; en 390 aborted; the others "ok" (see issue 11) | 21 | 1 and 2 (the fight frame: farmer sent home exhausted, furniture window open), 2 (1920 column over the heart), 6 (390 heart over the gauges) |
| market (list, buy, mail) | 8 | 6 of 6 ok | 30 | 2 (buy-confirm dialog cuts the filter row at desktop, 4 frames), 6 (phone seller name cut to "QaF_") |
| coop (found, members, stock, upgrades, log) | 10 | 6 of 6 ok | 60 | 1 and 3 and 4 (top-right column, minimap and hotbar are not drawn in any co-op frame at 1920; see issue 4) |
| bin + furnace | 9 | 6 of 6 ok | 48 | 1 (hint card floats mid-map, all six "after the sale" frames), 2 and 3 (emote heart under the column at 1920, over the gauges at 390), 4 (en 1280 furnace "Smelt Copper ore" button falls under its icon) |
| social (two players) | 16 | 1280x720 and 1920x1080 failed the co-op steps before this pass's harness fix; after it, all steps ok at both sizes, 390x844 ok throughout | 54 passing-run frames (`social-fixed/`, `social/390x844/`); the 8 `fail-*` frames of the first run are evidence, not graded | 6 and 7 (trade and co-op grids and the friends list clipped at desktop), 2 (chat card under the trade panel; menu column over the emote at 1920), 3 (phone menu not visible behind a panel); visit-farm and co-op log captures are stale (issue 9) |
| new player, first day (en 1280x720) | 6 steps, stopped at the bus stop | blocked; vi 390x844 not run | 30 | 1 (hint card over the world on almost every frame, issue 3), 2 (hint and toast overlap), 6 ("Profile" wraps in the menu) |

Totals: 355 matrix frames + 54 social frames + 30 new-player frames graded by eye (439 in all). The graders read every frame listed by
Glob; I spot-checked 9 frames myself (listed under "Verification" below).

## Fixed in this PR

| # | Issue | Evidence | Fix | Test |
|--:|:--|:--|:--|:--|
| 1 | Social harness failed at 1280x720 and 1920x1080 (co-op deposit and invite), both languages | `social/1280x720/fail-en-1280x720-coop-deposit-A.png`: the co-op window is on Members; the harness waited for the Deposit button that sits on Stock. Since #346 the co-op shows one part per tab at every size, but the script only clicked the tabs on a phone. | `tests/browser/social-round3-play.cjs`: the Members and Stock tab helpers click at every size | Re-run: `social-fixed/1280x720` and `social-fixed/1920x1080`, all steps ok in both languages. `node --check` passes. |

No UI defect was fixed in this pass. The top candidates (issues 2 and 3 below) need a design decision about what gives way on a phone or
where a hint belongs, so they are ranked rather than changed.

## Ranked issues (not fixed; most severe first)

1. **A new player cannot leave the farm.** The Bus Stop sign at the farm's top right answers "That does nothing here." with empty hands,
   and walking onto it hides the avatar behind the farmhouse corner. The world map lists Bus stop, Forest and Backwoods but they are not
   clickable, so town and the merchants are never reached on day 1. Evidence: `new-player/en/1280x720/01-q-bus-stop-press-empty-hand.png`
   (checked by eye: the top-right "-> Bus Stop" label and the toast are both there). Also reported by the playthrough agent, not yet
   verified: the watering quest did not complete after a watering (`03-c-water.png`, `03-clock-check-after-60s.png`), and a new player
   cannot buy a first crop, so a first-day sale is not possible.
2. **English at 390 wide: the top-right row overflows.** The calendar bar (`.hud-bar`, `flex-wrap: wrap`, `wasm/Nosum.Wasm.Controls/Layout/GameHud.razor.css`)
   wraps the settings gear onto a second row below the mail button, and in the HUD frames it lands under the Menu button.
   Evidence: `farm/en/390x844/03-carpenter-open.png` (checked by eye: the gear is on its own row under the mail icon); also
   `town/en/390x844/01-tailor-talk.png`, `farm/en/390x844/02-coop-built.png`. Seen in 12 of the 13 English 390 farm frames (the 01 frame shows the quest title instead) and in the English 390 town frames.
   Proposed fix: decide what gives way on a phone (calendar text size, coin badge width, or moving the gear into the Menu window), then
   re-capture at three sizes.
3. **A hint card floats mid-map over the world.** After a sale at the bin, the "after" hint sits over the farm at 1280x720, 1920x1080 and
   390x844, in both languages. In the new-player day it is on nearly every English frame from the first screen onwards ("Walk with WASD",
   "Select the hoe...", the bus-stop hints): `new-player/en/1280x720/01-a-in-game.png`, `04-c2-water-after-3s.png`, `01-x-approach-sign-from-right.png`.
   This is the most-seen defect of the pass. Sections 4 and 5 of the standard put hints in a notice region, not over the world. The same applies to the
   placement confirmation ("Đã đặt xuống." / "Placed.") over the coop wall (`farm/vi/1280x720/02-coop-built.png`), whose source is not known
   from the frame. Evidence: `bin/vi/1280x720/05-bin-after.png`, `bin/en/1280x720/05-bin-after.png`, `bin/vi/390x844/05-bin-after.png`.
4. **The co-op board at 1920x1080 hides the top-right column, the minimap and the hotbar.** None of the 60 co-op frames shows them, and
   the same flow's town frames at 1920 do. Verified by eye in `coop/en/1920x1080/01-coop-board-open.png`. Cause unknown: it may be a
   HUD-hiding state of the co-op window or a capture artefact. It needs a trace (which state hides the HUD) before anyone fixes it.
5. **The emote heart is under the 8th column button at 1920 and over the gauge tops at 390.** Evidence: `bin/vi/1920x1080/01-bin-coin-pop-0.png`
   through 05, `farm/*/1920x1080/07`, `bin/vi/390x844/01-bin-coin-pop-0.png`, `mine/vi/390x844/01-elevator-open.png`.
6. **The wardrobe's worn list is cut off at desktop sizes, with an inner scroll fade.** Only the first one or two worn slots show, and the
   in-hand panel in the bag view is cut off at 1920. Points 6 and 7. Evidence: `town/vi/1280x720/03-wardrobe-open.png`,
   `town/en/1920x1080/03-wardrobe-open.png`, `town/vi/1920x1080/05-wear-hat-from-bag.png`.
7. **The buy-confirm dialog covers the market's filter row at desktop.** Its top edge cuts through the search and sort row, leaving slivers.
   Evidence: `market/vi/1280x720/03-A-buy-confirm.png`, `market/en/1920x1080/03-A-buy-confirm.png`.
8. **The save-account dialog has no close button.** It has a title bar but no X, unlike GamePanel (point 5). Evidence: all six
   `account/*/02-link-prompt.png`.
9. **The phone quest title loses its first letter under the scroll icon.** The English 390 quest card reads "ill the soil" (`farm/en/390x844/01-farm-start.png`).
10. **Furnace copy and feedback.** After a batch the furnace is empty with no toast, count or collected item (`bin/*/08-furnace-wait-and-collect.png`,
    all six). The copy says "1 coal" per batch with no coal slot in the panel (`bin/vi/1280x720/06-furnace-place.png`). In English at 1280 the
    "Smelt Copper ore" button drops under its icon while the iron row does not (`bin/en/1280x720/06-furnace-place.png`).
11. **The mine flow does not reach floor 2 in three of six runs, and one run aborted.** The descent step recorded "no ladder took the farmer down
    to floor 2 (9 to 12 far stones tried)". The frames show the farmer on the surface at a pond or grass (`mine/en/1280x720/02-descend-floors.png`,
    `mine/vi/1920x1080/02-descend-floors.png`), so the ore and fight steps ran on the surface. The en 390 run aborted with "Target page ...
    has been closed". Also, the fight step recorded ok but the frame shows the farmer exhausted and sent back to the farmhouse with a furniture
    window open (`mine/vi/1280x720/04-fight-monster.png`, checked by eye). This is a harness coverage gap, not a verified game bug, but a
    real regression from pass 8's "2 skipped" is possible; it needs a trace.
12. **Phone market: the seller name is cut mid-word** ("QaF_" in vi, "QaFr_" in en), `market/*/390x844/03-A-buy-confirm.png`.
13. **The minimap card's right edge sits about 10 px inside the top-right row and the menu column at 1920** (`farm/*/1920x1080/01`, `town/*/1920x1080/01`).
14. **The coin badge and the toast disagree after a sale.** In `bin/vi/1280x720/01-bin-coin-pop-0.png` the badge reads 500 and then 730 while the toast says 200;
    stable at 730 in frames 02 to 04. Probably a count tween; not verified.
15. **Copy and wrapping.** Orphans: "another." (`farm/en/1280x720/05-buy-cat.png`, `06`), "nhé." (`farm/vi/1920x1080/11`, `13`); split
    "Reward: 20 / coins." (`farm/en/1280x720/08-day-rolls.png`, `12`).
16. **The new player gets no feedback about rain.** Rain waters the soil silently, and watering then says "The soil is already wet today." (agent
    report, no frame kept). Low.
17. **A withered crop gives no cue why.** After watering a sown seed the message is "The crop withered from lack of water or the end of its
    season." (agent report, `new-player/en/1280x720/02-g-water-planted.png`). Partly the clock jump. Low.
18. **The hotbar shows tool icons only, and the tool in hand is named only in the hint and the inventory** (agent report,
    `new-player/en/1280x720/02-b-till.png`). Low.
19. **Open panels hide the quest card, the minimap, the column and the hotbar at every size.** By design or not, it is not stated in the standard.
    Low; observation only.
20. **Social harness (fixed in this PR, issue 1 in "Fixed").** Listed here because the failing run was the last evidence of stale co-op steps.
21. **Trade and co-op grids clip a row, and the friends list clips at its bottom (desktop).** The fourth trade row and the friends list are cut
    with a fade, an inner scroll at 1280x720 and 1920x1080 (points 6 and 7). Evidence: `social-fixed/1280x720/en-1280x720-trade-open.png`,
    `social-fixed/1920x1080/en-1920x1080-trade-open.png`, `social-fixed/1280x720/en-1280x720-friends-online.png`,
    `social-fixed/1920x1080/vi-1920x1080-coop-deposit-receiver.png`.
22. **The chat card runs under the trade panel** (point 2) at 1280x720 (`social-fixed/1280x720/vi-1280x720-trade-invite-receiver.png`), and the
    menu column's last button covers the emote at 1920 (`social-fixed/1920x1080/vi-1920x1080-chat-received.png`).
23. **The menu label "Profile" wraps** with "N" alone on the second line (`new-player/en/1280x720/01-z-menu-open.png`, point 6).
24. **Two social captures are stale, and the harness hid why.** `visit-farm` frames are dark with no farm (`social-fixed/1280x720/en-1280x720-visit-farm.png`,
    `social/390x844/en-390x844-visit-farm.png`), and `coop-log` frames show the world with no co-op panel
    (`social-fixed/1280x720/en-1280x720-coop-log.png`, checked by eye). The co-op log step ends with `.catch(() => {})`, so a failed reopen is silent.
    Fix in the harness: make the reopen a checked step. Not changed here.

## Verification

- Spot-checked by eye (me, not the agents): `new-player/en/1280x720/01-q-bus-stop-press-empty-hand.png`, `farm/en/390x844/03-carpenter-open.png`,
  `mine/vi/1280x720/04-fight-monster.png`, `coop/en/1920x1080/01-coop-board-open.png`, `social/1280x720/fail-en-1280x720-coop-deposit-A.png`,
  `social-fixed/1280x720/en-1280x720-coop-log.png` (the stale world frame, issue 24). Each matched the grader's note.
- Matrix flows read by subagents, one per flow group, every PNG listed by Glob. Their tables and rankings were checked against my spot-checks
  and against each other: the English 390 overflow was reported independently by three graders and is visible in the frames I opened.
- The bin flow ran in about four minutes for all six combinations, not the 17 minutes per run pass 8 noted. The furnace collect step still
  passed (`furnace-wait-and-collect` ok in all six). The shared dev clock may have advanced; not investigated.

## Gaps (not driven)

- Mine floors beyond 2, ore yield, and the fight outcome (issue 11).
- The new player's day past the bus stop: town, the merchants and the sale were not reached in English, and Vietnamese 390 was not run.
- Co-op: a second member's withdraw and the log of another member were not driven.
- Phone touch targets were not measured (the pet dialog buttons may be under the 42 px minimum, per the farm grader).

## Reproduce

```
# server (Development, PostgreSQL role nosum, database nosum_dev)
DOTNET_ENVIRONMENT=Development dotnet run --no-build --project server/Nosum.Host
# tester client (ADR-079)
dotnet run --no-build --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080

NOSUM_QA_OUT=docs/qa/world-pass9 node tests/browser/world-pass8.cjs farm|town|account|mine|market|coop|bin vi,en 1280x720,1920x1080,390x844
BASE=http://localhost:5080/ LANGS=vi,en SIZE=1280x720 OUT_DIR=docs/qa/world-pass9/social-fixed/1280x720 node tests/browser/social-round3-play.cjs
```
