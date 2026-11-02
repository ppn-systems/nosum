# World QA pass 6 — Nosum (branch `claude/fix-qa5-farm`, base `origin/master` 8ef84b8)

**Read this first: this pass is partial.** The brief asked for the farm fixes, the clock helper, the level-up fix and the parsnip docs (done in
this PR, with evidence below), and then an extended harness covering animals, pets, station crafting, market, mail, co-op deposit, backpack,
shipping, mines, two-context social, wardrobe, guest progress and the world map, run at 1280×720 and 390×844 in vi and en, graded by eye.
**The extended harness was not written, and no extended frames were graded.** The list of what is missing is in "Not covered in this pass".

## What changed in this PR

| # | Area | Change | Evidence |
|--:|:--|:--|:--|
| 1 | Farm and quest | Investigated; see "Farm loop findings". Added `FarmWireTests.Watering_APlantedSeed_PushesTheWaterQuestAsCompleted` (the water quest completion is pushed as its own `QuestUpdate`). | New test passes; 22 existing farm wire tests pass on the base. |
| 2 | QA clock | `window.nosumQa.advanceDays(n)` and `advanceSeasons(n)` added; `advanceClock` kept as a days alias. `world-pass.cjs` uses `advanceDays`. Comment corrected: a season is 28 game days, so 4 + 10 days is half a season. | Build 0 warnings. |
| 3 | Level-up banner | The banner and the message line followed the combat channel's own level-up event in addition to the server's `LevelUp` packet, so each combat level showed twice. The packet is now the single source. | `LevelUpBannerViewModelTests`: a combat level that arrives as packet + channel event is shown once. |
| 4 | Parsnip docs | `docs/design/roadmap.md` said winter is forage only; the catalog has parsnip in winter (ADR-018). Corrected pass-5's row 18, which wrongly said pass 3 said "Spring only" (it did not). | Doc diff. |

## Farm loop findings (pass-5 issue 2)

Probe: the server database (`nosum_dev`, via `psql`) read after each browser step, plus screenshots. Setup: Development host, tester client,
guest sign-in, `springReset`, teleport to the farm tile (46,18), then hoe, sow, water.

- **Server, sow:** `farm_plots` has `crop_id = 1` at (48,18) right after the sow. The client shows "Seeds planted." Correct.
- **Server, water:** `watered_day` set; the client shows "Watered." Correct.
- **Server, quests:** the water quest (`WaterCrop`, id 3) is `state = 2` (Completed) with progress `{1}` after the water. The quest-log request
  agrees. The new wire test shows the completion is also pushed as a `QuestUpdate`. So the server and the push are correct.
- **Client quest card:** the card shows the first active quest, not the water quest, so "Water a patch" was not on screen in these runs. The
  pass-5 report ("stays 0/1 after Watered") was probably seen on a quest panel or on a run where the watering hit bare soil. Not reproduced
  on screen this pass.
- **Crop on screen: not resolved.** The sown tile is hard to read at 1280×720. The target cursor sits on the faced tile, the farmer's held
  item overlaps the tile east of the farmer, and parsnip's first stage is 34 opaque pixels in `crops.png` (so the sheet is not blank).
  The next step is a probe that moves the farmer off the tile and screenshots at 1920×1080, plus a check that `FarmDecorator` draws the
  row for a stage-0 crop. Not done.

## Clock units (pass-5 issue 14)

Pass 5 said `advanceClock(4)` then `advanceClock(10)` moved the clock one whole season. That is wrong: a game day is 12 real hours and a
season is 28 game days (`GameCalendar.DaysPerSeason`), so 14 days is half a season. That candidate cause is ruled out. The cause of the
season-label mismatch (the label read Fall after a Spring reset in two pass-5 probes) is still open. Fresh probes this pass read
"Spring Day 15" and "Spring Day 16" after `springReset`, so the mismatch did not recur in these runs.

## Remaining issues (carried from pass 5, still open)

From `docs/qa/world-pass5/README.md`, not re-graded this pass:

1. **Phone top-right shows two column buttons** under the mail and settings row instead of one Menu button (ui-standard §6). High.
2. **Farm crop visibility** (above): not resolved. High until a screenshot at stage 0 shows the sprout.
3. **Minimap still visible after M** in the audit's pass; the check measures height, not visibility. High.
4. **Phone panels** (crafting, friends, skills) are opened by the harness through the hidden column button; fix in the harness. Harness.
5. **1920×1080 English settings** clips the last line and the menu page scrolls. Medium.
6. **Phone shop header and touch targets** under 42 px. Medium.
7. **Clipped Vietnamese season text** in the seed shop at 1280 and 1920. Medium.
8. **Hint toast over the joystick** at 390. Medium.
9. **Opaque band at the bottom of the phone** when a window is open (reported, not verified). Medium.
10. **NPC and merchant dialogs have no close control** other than Leave. Medium.
11. **Level-up banner covers the furniture dock** at 1280×720 (audit layout). Medium; the banner fix above does not change placement.
12. **Level-up text** repeated four times (banner, world float, toast, chip). Partly fixed: banner and toast now show once; the world float and the HUD chip are unchanged.
13. **Co-op key (G)** opens a different panel than the warehouse list. Medium; not traced.
14. **Season label mismatch** (see above). Open.
15. **Low-contrast quest card header** at 390. Low.
16. **Icon-only Menu button and sticky tooltip** on touch. Low.
17. **Float and tooltip overlaps** at 390 and 1920. Low.

## Not covered in this pass

Not written, not run, not graded: animals (barn, feed, harvest), pets (carpenter, rehome), station crafting (chest, furnace), market buy,
mail send, co-op warehouse deposit, backpack upgrade, shipping bin, mines, two-context social through `befriend()`, wardrobe (C), guest
progress, world map. The pass-5 harness (`tests/browser/world-pass.cjs`) was not re-run in full for this PR. Only the farm probe above ran.

## Reproduce

```
DOTNET_ENVIRONMENT=Development dotnet run --no-build --project server/Nosum.Host
dotnet run --no-build --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080
node tests/browser/farm-loop-play.cjs 1280 720 en-US <outDir>   # stale selector; see the probe notes above
dotnet test tests/Nosum.IntegrationTests/Nosum.IntegrationTests.csproj --filter FullyQualifiedName~FarmWireTests
```
