# World QA pass 10 (branch `qa/world-pass10`, base `origin/master` 3bcd8c4, after #359)

Pass 9 (`docs/qa/world-pass9/README.md`) left 20 ranked issues. Pass 10 runs the whole world harness again on current master, at 1280x720,
1920x1080 and 390x844, in Vietnamese and English, plus a new player's first day. Every frame was graded by eye against the 10-point
checklist of `docs/design/ui-standard.md` section 7 by a grader, and the decisive frames were opened and checked again by the lead.
The Development server and the tester build (`-p:NosumDevTools=true`) ran the whole time; the matrix ran one flow at a time because
the dev clock is shared.

This PR changes harness code only. No product UI was changed. The earlier pass-10 write-up (the fixes for pass-9 items 9, 11, 21, 22, 23)
moved to `docs/qa/world-pass10/fixes/README.md` with its evidence.

Fixes to the ranked issues below are in [`ranked-fixes/README.md`](ranked-fixes/README.md) (issue 4 is left for an owner decision).

## Result

Harness results are the scripts' own checks (`ok` means the check passed). Frame counts are the PNGs graded by eye.

| Flow | Script | Steps per run | Harness result (6 runs) | Frames | Frames failing the checklist |
|:--|:--|--:|:--|--:|:--|
| farm (animals, pets, carpenter) | `world-pass8.cjs farm` | 15 | 6 of 6 ok, 0 page errors | 78 | 25 |
| town (tailor, wardrobe, backpack, bag) | `world-pass8.cjs town` | 8 | 6 of 6 ok | 42 | 7 |
| account (profile, save link, sign out) | `world-pass8.cjs account` | 3 | 6 of 6 ok | 18 | 7 (6 are the sign-out dialog, a judgment call) |
| mine (elevator, floors, ore, fight) | `world-pass8.cjs mine` | 4 | 5 of 6 runs with failed steps, 14 failed step-runs | 24 | 8 (furniture view), plus 8 frames on the surface |
| market (list, buy, mail) | `world-pass8.cjs market` | 8 | 6 of 6 ok | 30 | 22 |
| coop (found, members, stock, upgrades, log) | `world-pass8.cjs coop` | 10 | 6 of 6 ok | 60 | 23 |
| bin + furnace | `world-pass8.cjs bin` | 9 | 6 of 6 ok | 48 | 14 |
| social (two players) 1280 / 1920 / 390 | `social-round3-play.cjs` | 18 per size | before the fix: `coop-log` failed at 1280 and 1920 in both languages, 390 ok. After the fix: 18 of 18 at all three sizes | 20 per size | see issue 1 |
| new player, first day | `world-new-player.cjs` (new) | 6 to 8 per run | 1280 and 1920 ok in both languages after the fix; 390 fails the map steps (harness gap, issue 2) | 50 | see issue 2 |

Frames: 360 matrix frames (farm 78, town 42, account 18, mine 24, market 30, coop 60, bin 48, social 60), 50 new-player frames from the
fixed run, and 48 from the first new-player run, kept in `new-player-run1-harness-bug/` as evidence of the harness bug (see below). The farm grader's report, the town, account, mine, market, coop and bin reports, and the
new-player report were each checked against the lead's spot-checks listed under "Verification".

## Ranked issues (most severe first)

Frame paths are relative to `docs/qa/world-pass10/`. "Pass 9" refers to `docs/qa/world-pass9/README.md`.

1. **A new player's first day cannot be shown to leave the farm (unverified, pass-9 issue 1).** The harness never reached the bus stop
   sign in a run: the farmer stood at the farmhouse door, and the only feedback was "Till the soil first.". The sign's coordinates were not
   known, so the press at the door tile tested nothing. At 1280 the map button sits in the column, which is hidden while the menu window is
   open; the harness now closes the menu first, and the map opens at 1280 and 1920.
   Frame: `new-player/en/1920x1080/09-06-bus-stop-press.png` (checked by eye: "Till the soil first.", farmhouse door, no travel).
   Status: **not fixed, not verified.** Next: find the bus stop tile (the `→ Bus Stop` label is near the top-right of the map) and
   drive the press there.

2. **Phone (390) new-player run cannot reach the world map (harness gap, cause not confirmed).** The harness's map click failed at 390 in
   both languages (`04-world-map`, then `04b-map-closed`). The menu window at 390 shows the same three tiles as at desktop, so the map
   entry point at phone size was not located. Frame of the failure:
   `new-player-run1-harness-bug/en/390x844/` (the first run; the fixed run has the same steps). Not fixed: the phone's map entry point
   needs a look at a 390 frame with the Menu closed.

3. **Floating hint and notice cards sit over the world (pass-9 issue 3, still present).** Walk and till hints are Blazor cards in the
   middle-lower play area, not in the toast line. Frames: `farm/en/1280x720/01-farm-start.png`, `farm/en/1280x720/02-coop-built.png`
   ("Placed." over the coop wall), `farm/*/*/08-day-rolls.png`, `farm/*/*/12-day-rolls-again.png`, `bin/*/*/04-bin-coin-pop-3.png`
   (the hoe hint covers the farmhouse at 1920), `new-player/en/1920x1080/09-06-bus-stop-press.png`. Points 1 and 2.

4. **At 1280x720, with any window open, the minimap, the menu column and the hotbar are not drawn (undocumented, pass-9 item 19).**
   Frames: `town/*/1280x720/02..07`, `market/*/1280x720/01..05` (10 frames), `coop/*/1280x720/01..10` (20 frames). Checked by eye in
   `market/vi/1280x720/01-B-list-open.png`: the market window is open, the top row is drawn, the column and minimap are not. At 1920 the
   same flows draw them. Points 1, 3 and 4. The standard does not say whether a desktop hide is intended, so this needs a design
   decision before anyone changes it.

5. **At 1920x1080 the co-op window runs under the minimap card, and the hotbar is not drawn (pass-9 issue 4, partly changed).** The column
   is now drawn. The minimap is drawn but covered by the window. The hotbar is missing in all 20 frames. Checked by eye in
   `coop/en/1920x1080/01-coop-board-open.png`. Points 2 and 4.

6. **The furniture window in the mine is anchored top-left, not centred, and its tab row is clipped.** Furniture tabs after "Tủ kệ" (vi)
   and "Storage" (en) are cut off; "Cây c" is cut at 390. Checked by eye in `mine/vi/1280x720/03-fight-monster.png`. Frames:
   `mine/vi/1280x720/02..04`, `mine/vi/390x844/02..04`, `mine/en/1920x1080/03..04`. Points 1 and 6.

7. **Inner clipping of panels at 1920 (pass-9 item 7 and a new one).** The Vietnamese profile body runs into a scroll fade beside the
   sign-out button: `account/vi/1920x1080/01-profile-open.png` (checked by eye). The "In hand" description in the inventory is cut off
   under a fade at the bottom edge: `town/en/1920x1080/05-wear-hat-from-bag.png` and `town/vi/1920x1080/05-wear-hat-from-bag.png`.
   Points 6 and 7.

8. **The sign-out confirm has no close control.** `account/*/*/03-sign-out-question.png` (six frames). The save-account dialog now has
   its X (pass-9 item 5 fixed, all six `account/*/*/02-link-prompt.png`). Judgment call on point 5; the confirm is a small question, so
   it may be exempt.

9. **Orphan words and bad wraps.** "another." (`farm/en/1280x720/06-carpenter-after-buys.png`, `farm/en/390x844/06-...`), "nhé."
   (`farm/vi/1920x1080/11-pet-pet-today.png`, `13-pet-open-next-day.png`), "xu." (`farm/vi/390x844/08-day-rolls.png`, checked by eye),
   "deposit." (`coop/en/1280x720/06-coop-deposit-seeds.png`, `07`), "building." (`coop/en/390x844/04-coop-upgrades.png`), "row."
   and "ô." (`town/*/1280x720/02-backpack-upgrade.png`), "it." (`mine/en/1920x1080/03..04`), "xóa." (`market/vi/390x844/05-B-mail-collect-all.png`).
   Point 6. Pass-9 issue 15, still present.

10. **The furnace says "1 coal" per batch and has no coal slot.** `bin/*/*/06-furnace-place.png` (all six). Content, not a checklist
    point. Pass-9 item 10 (furnace collect feedback) is fixed: the in-panel line "Collected the bar." shows in all six
    `bin/*/*/08-furnace-wait-and-collect.png`.

11. **The emote heart touches the gauge tops at 390 (borderline).** `bin/*/390x844/01..05` (grader measured 0 to 5 px). At 1920 the heart
    is clear of the column (about 10 px). Point 2, borderline.

12. **A touch build shows the keyboard hint.** "Walk with W A S D or the stick on the left." at 390 in every new-player frame
    (`new-player/*/390x844/01..04`). Copy for touch players, point 10 and the i18n rule.

13. **Phone details (minor).** The bag panel's bottom-right corner touches the heart gauge at 390
    (`town/en/390x844/05-wear-hat-from-bag.png`); the Menu button is not visible in any market 390 frame (`market/*/390x844/01..08`,
    intent unknown); the 1280 column is about 4 px right of the minimap and top row (`bin/vi/1280x720/01-bin-coin-pop-0.png`); the
    hotbar count badges overlap the icon bottoms at 1280 and 1920 (`new-player/en/1280x720/01-01-farm-start.png`).

14. **The coin badge tweens 700 to 730 after a sale (pass-9 item 14, unclear).** `bin/vi/1280x720/01-bin-coin-pop-0.png`. The toast says
    200, the sale amount, so it is not a direct contradiction. Could be an intended tween; not verified.

15. **Mine flow does not reach floor 2 in 5 of 6 runs (pass-9 item 11, harness gap).** `descend-floors` fails with "the farmer walked out
    of the mine" and "no ladder took the farmer down to floor 2 (24 far stones tried)"; `ore-mine` and `fight-monster` then run on the
    surface. Eight frames show the farmer at home with the furniture window open (see issue 6). Not fixed in this PR; it needs a trace
    (`DEBUG_TUNNEL=1`, as in the previous pass) before anyone changes the walk.

16. **Wardrobe worn list not covered (pass-9 item 6 coverage gap).** No town frame opens the wardrobe Clothes tab, so the cut-off worn list
    was not re-tested. Harness gap, not a product result.

Lower-ranked observations, not counted: the transient "NEW SEASON" banner over the world (`farm/en/1280x720/01-farm-start.png`); the quest
card title is dark red on grey and reads faint (`farm/vi/1280x720/01-farm-start.png`); world labels "→ Bus Stop" / "↑ Farmhouse" sit in the
top-right HUD band (`bin/en/1920x1080/04-bin-coin-pop-3.png`).

## Pass-9 items

| # | Item | Status | Evidence |
|--:|:--|:--|:--|
| 1 | New player cannot leave the farm | **Not verified** (issue 1) | the bus stop press did not reach the sign; `new-player/en/1920x1080/09-06-bus-stop-press.png` |
| 2 | English 390 top row wraps the gear | **Fixed** | `farm/en/390x844/03-carpenter-open.png`, `town/en/390x844/01-tailor-talk.png` (one row) |
| 3 | Hint card floats over the world | **Still present** (issue 3) | as issue 3 |
| 4 | Co-op board at 1920 hides column, minimap, hotbar | **Partly changed** (issue 5) | column drawn; minimap covered; hotbar missing |
| 5 | Emote heart under the 8th column button at 1920 / over the gauges at 390 | **Fixed at 1920; borderline at 390** (issue 11) | `farm/vi/1920x1080/01-farm-start.png` |
| 6 | Wardrobe worn list cut off at desktop sizes | **Not observed** (issue 16, coverage gap) | none re-tested |
| 7 | Buy-confirm / 1920 vi profile fade | **Profile still present** (issue 7); buy-confirm not re-listed | `account/vi/1920x1080/01-profile-open.png` |
| 8 | Save-account dialog has no close button | **Fixed** | all six `account/*/*/02-link-prompt.png` show the X |
| 9 | Phone quest title loses its first letter | **Fixed** (per pass 10 fixes) | `farm/en/390x844/01-farm-start.png` reads "Till the soil +" |
| 10 | Furnace collect feedback and "1 coal" copy | **Feedback fixed; copy still present** (issue 10) | `bin/*/*/08` shows the in-panel line |
| 11 | Mine floor 2 not reached | **Still present** (issue 15) | 5 of 6 runs |
| 12 | Phone seller name cut mid-word | **Fixed** | `market/vi/390x844/03-A-buy-confirm.png` shows "Người bán: QaFriend3309" |
| 13 | Minimap edge 10 px inside at 1920 | **Fixed (by eye)** | `farm/en/1920x1080/02-coop-built.png`, `town/en/1920x1080/01` |
| 14 | Coin badge vs toast | **Unclear** (issue 14) | `bin/vi/1280x720/01-bin-coin-pop-0.png` |
| 15 | Orphan words | **Still present** (issue 9) | "Reward: 20 / coins." split is fixed |
| 16–18 | New-player rain feedback, withered-crop cue, hotbar names | **Not observed** in this pass's frames | no frame shows them |
| 19 | Open panels hide the column, minimap and hotbar at 1280 | **Still present** (issue 4) | `market/vi/1280x720/01-B-list-open.png` |
| 20 | Social harness fails at 1280 and 1920 | **Fixed in this PR** (harness) | see "Harness changes" |

## Harness changes in this PR

- `tests/browser/social-round3-play.cjs`: the `coop-log` step reopens the board from player A's tile when the Log tab is not visible.
  The invite step closes every window on A, so the co-op window was never open for this step. Before the fix the step failed at 1280
  and 1920 in both languages; after it, 18 of 18 steps pass at all three sizes (`social/1280x720`, `social/1920x1080`, `social/390x844`).
- `tests/browser/world-new-player.cjs` (new): a fresh guest's first day. It closes the menu before the map is clicked (the menu column is
  hidden at 1280 while a window is open) and closes the map before the press. The first version pressed the bus stop with the map
  open; its frames are kept in `new-player-run1-harness-bug/`. The current version still does not reach the sign (issue 1) and
  fails the map steps at 390 (issue 2, cause not confirmed). Both are recorded, not fixed.

## Verification

- Lead spot-checks, by eye, of grader findings: `farm/en/1280x720/01-farm-start.png` (hint floats), `farm/vi/390x844/08-day-rolls.png`
  ("xu."), `town/en/1920x1080/05-wear-hat-from-bag.png` (clipped), `account/vi/1920x1080/01-profile-open.png` (fade),
  `mine/vi/1280x720/03-fight-monster.png` (window top-left, tab clip), `market/vi/1280x720/01-B-list-open.png` (column and minimap hidden),
  `coop/en/1920x1080/01-coop-board-open.png` (window under minimap, no hotbar), `new-player/en/1280x720/05-03-menu-open.png` and
  `new-player/en/1920x1080/05-03-menu-open.png` (the same three menu tiles at both sizes, no World map tile on the menu),
  `new-player/en/1920x1080/09-06-bus-stop-press.png` (the press did not reach the sign).
- Grader reports are in the scratchpad (not committed): farm, town, account, mine, market, coop, bin, new-player.

## Gaps (not driven or not verified)

- The bus stop sign and its exit (issue 1); the phone's map entry point (issue 2).
- The mine floors past 1, ore yield, the fight outcome, and exhaustion (issue 15).
- The wardrobe Clothes tab (issue 16); the phone's bag panel corner, measured (issue 13).
- Touch target sizes at 390 and token-level spacing (point 9) were judged by eye only.

## Reproduce

```
# server (Development, PostgreSQL role nosum, database nosum_dev)
DOTNET_ENVIRONMENT=Development dotnet run --no-build --project server/Nosum.Host
# tester client (ADR-079)
dotnet run --no-build --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080

NOSUM_QA_OUT=docs/qa/world-pass10 node tests/browser/world-pass8.cjs farm|town|account|mine|market|coop|bin vi,en 1280x720,1920x1080,390x844
BASE=http://localhost:5080/ LANGS=vi,en SIZE=1280x720 OUT_DIR=docs/qa/world-pass10/social/1280x720 node tests/browser/social-round3-play.cjs
NODE_PATH=$(npm root -g) node tests/browser/world-new-player.cjs vi,en 1280x720,1920x1080,390x844
```
