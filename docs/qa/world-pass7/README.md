# World QA pass 7 (branch `claude/qa-pass7`, base `origin/master` 6338af7)

Pass 6 wrote its harness plan and did not run it. This pass fixes the three open farm and HUD items, runs the extended harness, and grades what it
captured. **Coverage is partial, and the list at the end says what is not covered.**

## Fixed in this PR

| # | Issue | Root cause (evidence) | Fix | Test |
|--:|:--|:--|:--|:--|
| 1 | Crop not visible at stage 0 after sowing | The stage-0 picture (frame 0 of every row in `crops.png`) is the pack's red sprout, and its colours are the soil's colours: the dark outline `(66,36,51)` against the dark soil `(62,42,33)` is a contrast of 1.02, and the other pixels are 1.0 to 1.3 against the soil browns. The server had the crop (`farm_plots`: crop 1 at 48,18, growth 0), and the client drew it (a before/after diff changes the tile). It was not hidden by the chunk bake (the ground is drawn first; the sprout is a world sprite), nor by the Front layer. | `tools/art_repack/crops.py` re-inks every opaque seed pixel by brightness as a green ramp (dark green outline, pale highlight). `crops.png` regenerated: only frame 0 of the 21 rows changed (1026 px). | `CropSeedArtTests` (every seed picture is green and has a sprout). |
| 2 | Level-up HUD chip repeats the level | The hotbar chip read `Level {N}!` from the same value every level-up feeds. That value also takes gathering levels (`AudioWiring` routes every `LevelUp` into the combat channel), so a farming level 2 showed "Level 2!" as if it were a combat level, under a banner that already said "Farming reached level 2!". | The chip shows the title only, `LEVEL UP!`. The unused `Combat_LevelUp_Badge` string is removed from both `.resx`. | `ProgressTests.CombatChannel_TriggerLevelUp_ActivatesRadianceBeaconAndHudBadge` now expects `LEVEL UP!`. |
| 3 | Season label mismatch (QA reliability) | `DevSeason.Seasons` has `[WireRange(1, 4)]`, so the server refused `Seasons = 0`, the "next Spring" request that `springReset()` sends. The packet was sent and the server never ran the handler, so the reset did nothing. Probe: from Fall, Summer and Winter the label stayed on the old season for 4 s (before the fix). The pass-5 "Fall after a Spring reset" was this. | Range `0..4` (ADR-079 amendment D10). No layout or opcode change. | `DevSeasonPacketTests` (range accepts 0); `DevQaWireTests.DevSeason_ZeroPasses_IsAnsweredAsTheNextSpring`. After the fix `springReset` reads Spring within 300 ms from each season (`season-label/season-label.json`). |
| 4 | Visitors page clipped at 1280x720 | The page is a size S window (100 u), so its rules text wraps past the bottom and scrolls with no visible cue. The `PanelSize` summary names "a visitor list" as an S example. | Visitors is M (160 u). The S examples in `PanelSize.cs` and `GameMenuWindow.razor` no longer name it. | Harness audit: no `scroll:` flag for Visitors at 1280 vi or en (re-run after the change). |

Re-verified, not redone: the phone Menu is one button with its tiles (`02-menu.png` at 390, vi and en). Other pass-5 items from PR #333 (minimap M,
settings at 1920, shop touch targets, season text in the shop, NPC close, quest contrast, touch tooltips) were not re-graded in this pass.

## Issue 1, the evidence

- `crop-stage0/tile-tilled-vs-sown-4x-en.png`: the tile before and after the sow, at 4x (after the fix). The sprout is the green leaves on the
  planted tile below the farmer. `sown-1280x720-en.png` is the full frame: the sprout is visible with no zoom.
- The earlier "before" frame was overwritten by the fix. Its colours are the numbers above: the sprout was the same colours as the soil and read as
  a dark box with no leaves. The night tint (the clock reads 12 AM to 2 AM in these runs, and the QA clock cannot change the hour) darkened both
  sides further. The fix does not depend on the time of day: the new ink is above the soil in luminance for the body and highlight pixels.
- Probe: `tests/browser/world-pass7-crop-probe.cjs` (sows one parsnip, captures the tilled and sown frames, prints the game's notices).

## Issue 3, the evidence

`tests/browser/world-pass7-season-probe.cjs` samples the calendar label after each QA jump:

| Call | Before the fix | After the fix |
|:--|:--|:--|
| `springReset` from Fall | Fall, still Fall at 4 s | Spring at 300 ms |
| `springReset` from Summer | Summer, still Summer at 4 s | Spring at 300 ms |
| `season(1)` from Fall | Winter at once | Summer at 300 ms (from Spring) |

The server log before the fix shows `DevSeason in=2`, `in=1`, `in=1` and no `in=0`. After the fix `in=0` is answered.

## Extended harness (issue 4)

`tests/browser/world-pass7.cjs` signs in as a guest on the Development server, resets to Spring, stands on the farm, then opens every menu tile and
every desktop column button (Mail and Settings too), one at a time, with a screenshot and an audit each (page errors, scrolling or clipped content
in the window, window off the viewport). It also presses G. Matrix: vi and en, 1280x720 and 390x844. Result: **0 page errors** in all four runs;
the audit flagged only Visitors (fixed above).

Graded by eye (the frames I looked at): 1280 en `03-Visitors` (clipped before, fixed), `16-key-g` (Co-op window, clean), `09-col-Crafting` (clean,
stale "Close" tooltip after the previous window's close control: low), `10-col-Trade` (empty state reads correctly); 1280 vi `12-col-Kỹ năng` (Skills,
clean), `03-Khách` (Visitors, clipped before the fix; re-run clean); 390 en `02-menu` (one Menu button, tiles fit), `04-Crafting` (fits),
`06-Visitors` (fits), `11-World-map` (fits, pages read "Page 1 of 3").

Captured and audited only, not graded by eye: the rest of the 40 frames in `{vi,en}/{1280x720,390x844}/`. Their flag state is in each `report.json`.

## Remaining issues (ranked)

1. **The level is named twice per level-up on screen** (medium): the banner says "Farming reached level 2!" and the toast says the same line. The
   banner was fixed in pass 6 so it shows once, but the toast is a second place with the number. Decide which one keeps it.
2. **The world float says `LEVEL UP!` with no number in code** (`Combat_Label_LevelUp`); the pass-6 report said it repeats the level. Not seen
   repeating in a capture this pass. If the float is meant to be the number-free celebration, drop it from the list; if not, it needs a trace.
3. **Stage-0 sprout is small** (11 px in a 16x32 frame, 13 to 35 opaque pixels). Visible at 1280x720 now. Not checked at 390x844 in a planted-tile
   capture. Art follow-up if it reads small on a phone.
4. **Sticky "Close" tooltip** after a window closes with its own close control (low): the tooltip stays over the HUD until the pointer moves. Seen
   in the desktop Crafting and Trade captures.
5. **Not covered by the harness** (medium, the brief's main gap): animals (barn, feed, harvest), pets (carpenter, rehome), station crafting (chest,
   furnace recipes), market buying, mail sending, co-op deposit, backpack upgrade, shipping bin, mines, two-context social (`befriend()`), wardrobe
   changes, guest progress. Each needs a scripted flow; the existing `animals-*`, `social-round3-play` and `farm-round4` scripts cover some of them
   and are the place to start. The panels for most of these were opened by the harness, but no action inside them was taken.
6. **Phone shop, settings and NPC close, season text, quest contrast, touch tooltips** (pass-5 list, fixed by PR #333): not re-graded in this pass.

## Reproduce

```
DOTNET_ENVIRONMENT=Development dotnet run --no-build --project server/Nosum.Host          # PostgreSQL role nosum, database nosum_dev
dotnet run --no-build --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080
node tests/browser/world-pass7-season-probe.cjs http://localhost:5080/?qa=1 docs/qa/world-pass7/season-label
node tests/browser/world-pass7-crop-probe.cjs http://localhost:5080/?qa=1 docs/qa/world-pass7/crop-stage0 1280 720 en
node tests/browser/world-pass7.cjs vi,en 1280x720,390x844
dotnet test tests/Nosum.Tests.slnx
```
