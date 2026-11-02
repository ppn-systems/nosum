# World QA pass 5 — Nosum (branch `claude/qa-world-pass5`, base `origin/master` fc98de0)

**Read this first: this pass is partial.** The brief asked for every feature (farming through minimap and world map, two-player social, sign-in
and guest progress) at six combinations. This pass **ran the existing world harness** (`tests/browser/world-pass.cjs`, 307 records) and graded
its screenshots by eye. It did **not** script the other features, so most of them have no evidence here (see "Not exercised").

Three kinds of evidence, kept apart as in pass 3:

1. **DOM checks** (`pass` / `FAIL` in `audit-<locale>-<viewport>.json`): what the script saw. These are weak where the check measures one thing and
   the screen shows another (see "Harness defects").
2. **Graded frames**: each screenshot read by eye against the 10-point checklist of `docs/design/ui-standard.md` section 7 (section 6 and the
   10-point list for phone). Six grader agents each graded one combination; their files are in `grades/`. Several of their claims were checked by
   the author against the frame; those are marked **verified**. Others are marked **reported** (a grader read it; not checked).
3. **Console and page errors**: none recorded in any of the six combinations (`console / page errors` rows, `pass`).

## Environment

- Code: `origin/master` at fc98de0 (PRs up to #330). No game code changed in this PR.
- Server: Development host (`ASPNETCORE_ENVIRONMENT=Development`), PostgreSQL 16, database `nosum`, role `nosum`.
- Client: `dotnet build wasm/Nosum.Wasm -p:NosumDevTools=true`, served on `:5080`, opened with `?qa=1` (ADR-079).
- Smoke: `tests/browser/smoke/boot-smoke.cjs` passed (43 frames, no page or console error).
- Harness change: `world-pass.cjs` writes to `docs/qa/world-pass5` (`NOSUM_QA_OUT` overrides). Nothing else in the harness changed.

## Results by combination

Counts are from the grader files (frames graded / PASS / PASS-WITH-NOTES / FAIL). Audit FAIL rows are the script's own DOM checks.

| Locale | Viewport | Frames graded | PASS | PASS-WITH-NOTES | FAIL | Audit FAIL rows | Console / page errors | Grade file |
|:--|:--|--:|--:|--:|--:|--:|:--|:--|
| vi | 1280×720 | 39 | 8 | 8 | 23 | 0 | none | `grades/grade-vi-1280x720.md` |
| vi | 1920×1080 | 39 | 4 | 14 | 21 | 1 | none | `grades/grade-vi-1920x1080.md` |
| vi | 390×844 | 40 | 0 | 0 | 40 | 4 | none | `grades/grade-vi-390x844.md` |
| en | 1280×720 | 40 | 10 | 20 | 10 | 1 | none | `grades/grade-en-1280x720.md` |
| en | 1920×1080 | 40 | 16 | 15 | 9 | 1 | none | `grades/grade-en-1920x1080.md` |
| en | 390×844 | 41 | 10 | 22 | 9 | 5 | none | `grades/grade-en-390x844.md` |

**The FAIL counts are dominated by two causes, not by many separate defects.** At 390×844 the phone top-right column defect (issue 1) fails every
frame at point 1; the vi-390 grader says that without it, 29 of 40 frames would be PASS-WITH-NOTES. At 1280 and 1920 the largest group is the
farm loop and the minimap (issues 3 and 4). The per-frame grades are in the grade files; they are not repeated here.

Audit FAIL rows (all six combinations): `farm: sow` (vi-1920, en-390: the sow was refused as out of season; see issue 14), `crafting panel`,
`panel: friends`, `panel: skills`, `panel: crafting` at 390 (harness, see harness defects), `level-up banner (QA) (layout)` covers the furniture
dock (en-1280), `settings: English (layout)` scrolls 785/740 (en-1920).

## Verified by the author (spot checks of grader claims)

- **Phone top-right (issue 1): verified.** `vi/390x844/01-HUD-start.png` shows the mail and settings row, then **two** stacked icon buttons in the
  column (about x 355, y 70 and y 100). Section 6 of the UI standard asks for one "Menu" button. The CSS hides `.hud-menu-item:not(.hud-menu-button)`
  on phones (`wasm/Nosum.Wasm.Controls/Layout/GameHud.razor.css`), so the second button is a column item that is not hidden. Which button it is
  was not traced.
- **vi-1920 shop frame, "stray stall icon": not confirmed.** The vi-1920 grader reported a stray icon under the top-right row in shop frames. In
  `vi/1920x1080/13-shop-seed-shop-buy-list.png` that icon is the Menu button (the chest-like icon under the bar). Treat that report as a misreading.
- **vi-1280 shop frame, "orphan menu button when a panel is open": not reproduced.** `vi/1280x720/13-shop-seed-shop-buy-list.png` shows the
  column still present (mail, settings and two column icons). The orphan claim (frames 12–21, 30–36) needs a trace before anyone acts on it.
- **Clipped text (verified).** `vi/1920x1080/13-shop-seed-shop-buy-list.png`: "Trồng vào Xuân, Th…" is cut mid-word in the Vietnamese seed row
  (the season list). The English claim "Plants in Sprin…" (en-1280 frame 14) is reported by a grader.
- **Stepper (verified in vi).** At 1280 and 1920 the vi seed shop shows `x1 x5 Tất cả Bớt [1] Thêm`. "Thêm" fits on one row at 1920 and 1280; it
  wraps onto its own row at 390 (reported in vi-390 and en-390).
- **Season label (verified as inconsistent; cause open).** In two probes (`probe/`): a fresh load read **Fall**; after
  `nosumQa.springReset()` the label still read **Fall** for 15 s. Sowing parsnip then said "Seeds planted", and parsnip grows in Spring, Fall and
  Winter (`CropCatalog.cs`), so the sow does not prove the season. Other frames in this same run read Xuân (vi-1280) and Hè (vi-1920). The cause
  is not found. See issue 14.

## Ranked issues

Severity: **High** = a feature is wrong or unverifiable in the game; **Med** = a visible layout or checklist failure; **Low** = polish. "Verified"
means the author checked the frame; "reported" means only a grader did.

| # | Sev | Where | Issue | Evidence | Status |
|--:|:--|:--|:--|:--|:--|
| 1 | High | phone, ≤640 px | Top-right shows **two** column icon buttons under the mail/settings row instead of one Menu button (ui-standard §6, §2 "8 buttons" and one column). The column collapse is incomplete. | `vi/390x844/01-HUD-start.png` (verified); `en/390x844/01` (reported) | Open |
| 2 | High | farm, all combos | **Farm loop not visible.** After sow, the advance-4-days and harvest steps show no crop on the plot; harvest reports "Something is in the way" / "Có vật cản ở chỗ này" on an open plot; the quest stays "Water a patch 0/1" after "Watered." | `vi/1920x1080/10-…`, `11-…`; `en/1280x720/10-12`; `en/390x844/08-11`; `vi/1280x720/08-11` (reported) | Open; needs a probe to tell a render gap from a game fault |
| 3 | High | minimap, all combos | **Minimap still visible after M.** The audit records `minimap toggle (M)` as pass because the card height changed; the screenshot after the toggle still shows the minimap. Either the check measures the wrong thing or the toggle does not hide the card. | `en/1280x720/04-minimap-toggle-M.png` vs `03` (reported, identical); `vi/1280x720/03` (reported) | Open; the pass in the audit is not evidence |
| 4 | High | phone | **Harness cannot open the phone panels.** Crafting, Friends and Skills are menu tiles (the menu lists them: "Chế tạo", "Bạn bè", "Kỹ năng"), but the script clicks the hidden column button, which fails "Element is not visible". This is a harness defect, not a missing panel. | audit rows `panel:` at 390 (all four combos) and the phone menu row, which passes with those tiles | Harness; fix in the script (open via the Menu tile) |
| 5 | Med | 1920×1080, en | Settings content clips at the bottom ("Show again from the start") and the menu page scrolls 785/740. Checklist 7 fails (no inner scroll at 1920). | `en/1920x1080/01-settings-English.png`; audit `settings: English (layout)` | Open |
| 6 | Med | phone, all | Shop header "Thêm" / "More" wraps alone onto a second row; touch targets in dialogs, steppers, market and co-op are under 42 px. | `vi/390x844/13`, `16`, `19`; `en/390x844/14`, `17`, `20` (reported); verified at 1280/1920 only | Open |
| 7 | Med | 1920 and 1280, vi | Text clipped in the seed shop: "Trồng vào Xuân, Th…" (season list, greyed rows). English: "Plants in Sprin…" (reported). | `vi/1920x1080/13` (verified) | Open |
| 8 | Med | phone | Hint toast overlaps the joystick (tutorial hint sits over the joystick ring, about x 85 vs 17–93). | `vi/390x844/09`, `10`, `11`; `en/390x844/07`–`11` (reported) | Open |
| 9 | Med | phone | Opening a window leaves an opaque dark band over the bottom of the screen (about y 715–844) that hides the hotbar and touch controls; the Menu title shows "Menu" in the vi set. | `vi/390x844/30`–`37` (reported); `en/390x844/22`, `31`, `33` (reported) | Open; confirm against §6 |
| 10 | Med | NPC dialogs, all | No close control on merchant and NPC dialogs (title shows only the name; "Leave" is the only exit); button widths uneven across dialogs. | `vi/1280x720/12`; `en/1280x720/13`, `16`, `19` (reported) | Open |
| 11 | Med | 1280×720 | Level-up banner covers the furniture dock (audit layout check). | audit `level-up banner (QA) (layout)`, en-1280 | Open |
| 12 | Low | level-up, all | The level-up text repeats four times (banner, world float, toast, chip); the "19" label sits on the bar on phone. | `en/390x844/41`; `vi/1920x1080/40` (reported) | Open |
| 13 | Low | co-op (G) | The co-op frame shows the founding form, or on en-1920 a furniture panel, not the warehouse list. Either the key opens a different panel or the script captures the wrong state. | `en/1280x720/36`, `en/390x844/37`, `en/1920x1080/36` (reported) | Open; trace which panel G opens |
| 14 | High (QA reliability) | farm, season | **Season label and sowing disagree, cause open.** The reset did not change the label in two probes; the sowing refusals ("does not grow in this season") and the sows that worked are both consistent with a clock that moves between runs. Candidate: the harness's `advanceClock(4)` then `advanceClock(10)` moves the shared server clock **14 game days, one whole season**, so each run leaves the clock one season on (`DevClockService`, 28-day seasons). **Ruled out in pass 6:** 14 days is half a season, so no season boundary is crossed by that call pair. The cause of the label mismatch is still open. | `probe/season-probe*.cjs`; `vi/1280x720` "Xuân", `vi/1920x1080` "Hè"; baseline `farm: sow` FAILs | Open; the harness must reset and check the season, not assume it |
| 15 | Low | en-390 | The quest card header is low contrast (light grey title on pale grey). | `en/390x844/03` onward (reported) | Open |
| 16 | Low | phone | The Menu button is icon-only (no text label), and the "Settings" hover tooltip stays on screen on touch. | `en/390x844/31`, `39` (reported) | Open |
| 17 | Low | level-up / combat | "CRIT -18" float touches the quest card at 390; the "Cầu lửa" tooltip covers the combo chip at 1920. | `en/390x844/29`; `vi/1920x1080/39` (reported) | Open |
| 18 | Low | docs | Parsnip's season was stated wrongly in the docs. Correction: `docs/qa/world-pass3/README.md` never said Spring only (this row was wrong about it); the stale line was `docs/design/roadmap.md` ("winter: forage only"). `CropCatalog.cs` is correct: Spring, Fall and Winter. | `gameplay/Nosum.Gameplay/Farm/CropCatalog.cs:83` (verified) | Fixed in the pass-6 PR (roadmap line) |

Not reproduced: the vi-1280 "orphan menu button" (see Verified). Not verified: the en-1280 "Minimap spills above its card" (reported at 1920).

## Harness defects (fixed or to fix)

- **Phone panel steps** click the hidden column button (issue 4). Open the panel from the Menu tile instead.
- **Settings step** leaves the Settings window open, so `02-HUD-start` is the Settings panel (identical to `01`). Close it before the HUD-start capture.
- **Quantity stepper steps** (`15`, `18`, `21`) are captured before any change, so they match the list frames. The audit's `x5 yes` is a text match, not a result.
- **The minimap check** measures a height change, not visibility (issue 3).
- **`farm: advance 4 days` + `wither (10 days)`** together move the shared server clock 14 game days. That is half a season (28 game days, docs/adr/ADR-018), not a whole one; the pass-5 claim in issue 14 was wrong. Pass 6 uses `advanceDays` and `advanceSeasons` explicitly.
- **Board keeper dialog** (`40`) opens Mai, the seed shopkeeper, because the NPC step reuses the seed shop tile. Name the step for what it opens.

## Not exercised in this pass

These were not scripted. There is no evidence in this pass for them, either way:

- animals (barn, feed, harvest) and pets (carpenter, rehome)
- crafting at stations (chest, furnace) and the chest and backpack upgrade
- market: list and buy (the market frame shows "No listings found")
- mail: send
- co-op warehouse deposit (see issue 13)
- shipping bin sale; dungeon and mine combat
- friends, visit, chat, trade and co-op with a second context (`befriend()`, ADR-105)
- wardrobe (C), settings beyond the language switch, guest progress and sign-in
- minimap and world map beyond the M key

The brief asked for all of these. Writing those paths is the next step; they need the two-context DevBefriend flow and the building and station
steps, which this pass did not write.

## Reproduce

```
DOTNET_ENVIRONMENT=Development dotnet run --no-build --project server/Nosum.Host
dotnet run --no-build --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080
node tests/browser/smoke/boot-smoke.cjs http://localhost:5080/ --timeout-ms 120000
node tests/browser/world-pass.cjs vi,en 1280x720,1920x1080,390x844
```
