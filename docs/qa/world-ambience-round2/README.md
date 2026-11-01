# World ambience, round 2

Evidence for the open items in [`docs/design/critique-world-ambience.md`](../../design/critique-world-ambience.md) (section 5, after
PR #293). Same method as round 1: a Development server with the calendar epoch set to the state, the tester client (`-p:NosumDevTools=true`,
`?qa=1`) in Chromium, 1280×720, English and Vietnamese. `before` is master at `be97af9` (PR #293); `after` is this branch.

## What was covered

| Item | Where | Result |
|:--|:--|:--|
| Fall season (round 1 skipped it) | `fall-noon-sun-*` (Fall Day 5, sun) | Leaves read as leaves on the farm. Graded, not changed. |
| Deeper mine floors (round 1 stopped at floor 1) | `fall-noon-sun-mine6`, `-mine15`, `-mine25`, `-mine35` | One floor per biome: Earth (6), Frost (15), Magma (25), Ruins (35). **Visual issue fixed:** magma labels stacked (ADR-091). One open issue on floor 15 (below). |
| Map-change fade | `fall-noon-sun-<map>-fade.png`, 120 ms after the teleport | The frame is the old map darkened, then the new map. Graded, not changed. |
| Exit signposts | `fall-noon-sun-exit-east`, `-exit-south`, `-exit-north` (the farm's exits) | The destination label is the cue ("→ Bus Stop", "↓ Forest", "↑ Backwoods"). The beacon is faint (30 % gold). Graded, not changed. |
| Snow and leaves | `winter-snow-farm`, `-town`; fall leaves in `fall-noon-sun-farm` | **Visual issue fixed:** one-pixel flakes were lost on grass; near flakes are now two pixels (ADR-027 amendment). |
| Audio mix | `../world-ambience-round2/audio-levels.md`, ADR-090 | Every file measured, one target per class, gain applied once at load. |

Frames: `before/` and `after/`, each with `en/1280x720` and `vi/1280x720`. Reports: `report-<scenario>-en+vi.json` (per frame, the HUD
calendar text, the audio buffers the page started in that visit, and any page errors: none in any report).

Calendar states (from the reports): `fall-noon-sun` is Fall Day 5, 2:12–2:43 PM (Vietnamese: "Thu Ngày 5"); `winter-snow` is Winter
Day 5, 2:32–2:53 PM.

## Audio, by map (from the audio log, not heard)

- Mine floors 1 and 6 are Earth: they play the spring day track (no new loop on floor 6, as ADR-070 intends).
- Floor 15 (Frost) plays the winter day track (34.29 s), floor 25 (Magma) the summer night track (30 s), floor 35 (Ruins) the fall
  night track (37.89 s).
- Every map change plays the door cue (0.32 s, once).

## Changes in this round

1. **Audio mix** (ADR-090). `tools/measure_audio.py` measures every WAV. `AudioMix` sets one target per class: music −16 dBFS, actions and
   stingers −16, footsteps −18, UI −18, rain bed −16. Each file's gain (target minus measured) is applied once, when it decodes
   (`audio.js` `applyGain`). Footsteps are now about 5 dB under the music (were about 9). Rain and storm keep their measured relation to the
   music (5.2 and 1.4 dB under). Tests: `AudioCatalogTests` (measured levels match the files; headroom below full scale; class order; gains
   within 12 dB).
2. **Hazard labels** (ADR-091). A hazard's overhead label is placed only if no label of the same text sits within 96 × 16 px this frame
   (`HazardLabelRules`; a fixed 64-entry array, no allocation). Frames: `before/en/1280x720/fall-noon-sun-mine35.png` (several "MAGMA
   FISSURE" labels stacked, one reading "SCMAGMA FISSURE") and `after/…` (one label per cluster). Tests: `HazardLabelRulesTests`.
3. **Snow flakes** (ADR-027 amendment). A near flake is 2×2 px, a far one 1×1. Frames: `before/en/1280x720/winter-snow-farm.png` and
   `after/en/1280x720/winter-snow-farm.png`. Test: `SkyRenderingTests.Precipitation_Snow_NearFlakesAreTwoPixelsSquareAndFarOnesOne`.

## Open, seen in these captures

- **Labels at the bottom edge run under the hotbar.** On `after/en/1280x720/fall-noon-sun-mine15.png` the shrine and trap labels at the
  bottom left ("VITAL" and "SPIKE TRAP") sit under the hotbar and run into each other. The engine cannot see the HUD layout (ADR-077 uses a
  measured inset), so the fix needs a HUD-aware rule. Not changed here.
- **The north exit label sits on the player's name tag** when the player stands at the exit (`before/en/1280x720/fall-noon-sun-exit-north.png`).
  They touch but do not overlap. Not changed.
- **The exit beacon is faint** (30 % gold, 10 px). The label carries the cue; a design call.
- **The minimap's unpainted cells** still read as a dark band (round 1, open for the design owner).
- **Listening** is not possible in a headless browser. The levels are measured, and ADR-090 says a person has to listen to the classes.

## Reproduce

```sh
# Postgres 16 on localhost:5432 (database nosum, role nosum); the client built with -p:NosumDevTools=true and served on :5080.
MAPS="farm:46,18;town:39,2;beach:39,43;forest:20,20;mine1:5,5;mine6:5,5;mine15:5,5;mine25:5,5;mine35:5,5;farm:74,16:exit-east;farm:40,61:exit-south;farm:40,2:exit-north"
EPOCH=2026-10-02T10:43:39.000Z SCENARIO=fall-noon-sun OUT_DIR=docs/qa/world-ambience-round2/after LOCALES=en,vi VIEWPORTS=1280x720 \
  MAPS="$MAPS" SCRIPT=tests/browser/world-ambience-capture.cjs bash tests/browser/world-ambience-run.sh
EPOCH=2026-09-18T10:40:40.000Z SCENARIO=winter-snow OUT_DIR=docs/qa/world-ambience-round2/after LOCALES=en,vi VIEWPORTS=1280x720 \
  MAPS="farm:46,18;town:39,2" SCRIPT=tests/browser/world-ambience-capture.cjs bash tests/browser/world-ambience-run.sh
python3 -I tools/measure_audio.py --csv audio-levels.csv
```

The epochs are relative to the capture date (`docs/qa/rebuild-world-ambience/README.md`): the fall epoch is the round-1 spring-noon epoch
moved back 30 days (60 game days of 12 hours), so the game clock reads the same minute on Fall Day 5. The weather on that day is sun (leaves in the frame); if
the day's weather changes, pick another epoch with the same minute and check the frame.
