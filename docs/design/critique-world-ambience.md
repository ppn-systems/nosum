# World ambience critique (rebuild-world-ambience)

**Status:** Reviewed on the `before` build (master at 384cb1d), then three changes made. Captures and the
numbers behind this review are in [`docs/qa/rebuild-world-ambience/`](../qa/rebuild-world-ambience/). Round 2 (section 6)
closed the fall, deep-mine, transition and snow gaps and set the mix from measurements. Owner decision still open: a
listening pass on the mix levels (section 5).

## 1. What was walked, and what was not

Run on a local Development server (Postgres 16) and the QA client (`-p:NosumDevTools=true`, `?qa=1`), in Chromium.

| Covered | Not covered (and why) |
|:--|:--|
| Five maps: farm, town, beach, forest, mine floor 1 (`mine1`) | Mine floors 2 and deeper; the other buildings (barn, coop, farmhouse, woods, mountain) |
| Five calendar states: spring noon sun, spring dusk, spring night rain, summer 15:00 storm, winter noon snow | **Fall** (no state sampled, so no fall leaves); a full 12-hour game day walked in real time |
| Viewports 1280×720 and 390×844, English (and Vietnamese for three states) | Touch play; keyboard walks between maps (the map change was a QA teleport, which runs the same fade and cue) |
| Audio: every buffer the page started in each map visit (duration, looped or once) | **Listening.** A headless browser cannot judge how a sound feels; loudness below is measured from the files, not heard |

Time of day, season and weather come from the calendar epoch (`NOSUM_CALENDAR_EPOCH`, ADR-018). The scripts are
`tests/browser/world-ambience-run.sh` and `tests/browser/world-ambience-capture.cjs`. Each capture report
(`report-<state>-<locale>.json`) lists the HUD calendar text it saw, so each frame is graded against its own state.

## 2. Findings, with frames

Frames are in `docs/qa/rebuild-world-ambience/before/en/<viewport>/`. "Fixed" means changed in section 3.

### Navigation and wayfinding

- **The minimap is coarse.** The ground is at most 50×34 px (`MinimapLayer.MaxWidth/MaxHeight`); a larger map is
  sampled, and dots are 2 px. Merchants, talkers and exits are hard to tell apart at a glance. `noon-spring-rain-farm.png`
  (top right). Partly fixed: merchants have their own dot (section 3.2). Exits keep the purple dot.
- **Talkers and merchants were one colour.** Every NPC was orange, including the seed keeper, tailor, blacksmith and
  carpenter, who are the reason a player opens the minimap. Fixed (3.2).
- **Unpainted map cells look like a broken frame.** Cells with no ground are not drawn, so the dark frame shows
  through as a black band around the town and the farm (`minimap-town-before-after-4x.png`, left). This is accurate
  (the map is that shape) but reads as an error. Not changed: it is a palette decision for the design owner.
- **Phones hide the minimap by default** (`MinimapViewModel.Hidden`), and the world-map button stands in. On
  `noon-spring-sun-farm.png` (390 px) the view is clean and the touch controls are readable.
- **Map changes fade**: 0.2 s out, 0.25 s in (`MapScene.FadeOutSeconds/FadeInSeconds`), and each change plays the door
  cue (audio log: 0.32 s "once" at town, beach, forest and mine). Not judged on screen; the code path is unchanged.
- **Exit signposts** (`WayfindingDecorator`) draw beacons and labels at exits. Not walked to each exit in this pass.

### Day and night readability

- **Dusk is readable.** `dusk-spring-sun-beach.png` (19:13): the warm tint is clear and the water reads as water.
- **Night plus rain crushed the world.** The rain added 28 opacity on top of a 168 night tint, so the rain-night tint
  was 196/255. `night-spring-rain-town.png` (22:16) is nearly all dark blue-grey: the tree, rocks and the path
  are hard to pick out. From the tint formula, a mid-green ground pixel (100, 160, 80) is about 52 in luminance on a
  rainy night against about 65 on a clear one. Fixed (3.1).
- **Night on its own** (no rain) was not graded from a frame of its own: the sampled night frame had rain. The lamp
  holes (ADR-027 D5) were not in any sampled frame, so they are unverified. Not changed.

### Weather feel

- **Rain is light and readable.** `noon-spring-rain-farm.png`: thin blue streaks, the weather icon in the calendar
  bar, a faint cool grey on the day.
- **A storm reads as light rain.** The storm used the rain's upright 1×5 streak at 1.4× the count, in the same colour,
  so `storm-summer-storm-town.png` looks like a heavier shower, not a storm. Fixed (3.3).
- **Lightning works.** The audio log shows a 1.6 s "once" cue (thunder) during the storm state.
- **Snow and autumn leaves** were not graded from frames in this pass.

### Music, sound effects and loudness

Loudness is **measured from the committed files** (`tools/` stdlib script, peak and RMS dBFS, before any bus gain):

| File | Peak | RMS | Note |
|:--|--:|--:|:--|
| music (eight tracks) | −2.6 | −13.6 to −16.2 | loops of 30–38 s |
| rain loop | −6.0 | −15.6 | played at 55 (rain) or 85 (storm) |
| thunder | −0.9 | −14.3 | one-shot, played at 80 |
| harvest / coin | −3.1 / −4.4 | −14.1 / −16.0 | at 100 |
| footstep | −9.1 | −23.8 | played at 70 |
| UI click | −8.0 | −19.9 | played by the browser (`audio.js` DOM listener); the click's volume is not a catalogue value |

Both buses default to gain 0.49 (−6.2 dB, `audio.js`). Effective RMS: **music ≈ −21.6**, **rain ≈ −27.0**,
**storm ≈ −23.2**, **footstep ≈ −33**. So rain sits about 5 dB under the music, which is a reasonable bed, and a storm
about 1.6 dB under it. Footsteps sit about 11 dB under the music's RMS and about 10 dB under its peaks. That may be
too quiet for walking to be felt, but this is a judgement that needs ears. **Not changed.**

- **Variety:** eight music tracks (season × day/night); mines reuse four of them by biome (ADR-070 D3). A map change
  does not restart the music (the audio log shows no new loop between farm and town, as intended).
- **Crossfades** exist for music and the rain loop (`audio.js` layer fade). The log shows one start per cue, with no
  double triggering.

### Minimap and world map

- Shops (buy-capable NPCs) and talkers now have different dots (section 3.2). The world map (`WorldMapViewModel`) was
  not walked in this pass.

## 3. Changes made

Each change is inside an existing system. No new interop call, no new allocation, no new packet, no new file in the
frame path (`review-gates`).

3.1 **Weather tint scales with the light that is left** (`gameplay/Nosum.Gameplay/Weather/AmbientLight.cs`). At noon
   the weather adds its full 28 (or 42 in a storm), as before. At dusk and night it adds only its share of the light
   left (`extra × (255 − A) / 255`), so a rainy night is a little darker than a clear one (A 177, not 196). Tests:
   `AmbientLightRulesTests` (weather adds less at night than at noon; a storm night stays at or below 200).
   *Frames:* `before/en/1280x720/night-spring-rain-town.png` → `after/en/1280x720/night-spring-rain-town.png`.
3.2 **Merchants get a white minimap dot** (`wasm/Nosum.Wasm.ViewModels/Minimap/MinimapMarkerRules.cs`, used by
   `MapScene.Minimap.cs`). An NPC with `NpcActions.Buy` is white; a talker stays orange. Tests:
   `MinimapMarkerRulesTests`. *Frames:* `minimap-town-before-after-4x.png` (the white squares are the merchants).
3.3 **Storm drops lean** (`engine/Nosum.Rendering/PrecipitationLayer.cs`). A storm drop is two 1×3 segments, the lower
   one a pixel to the left, which matches the storm's wind drift. Rain is unchanged (one 1×4 rectangle). The bottom
   segment is clamped to the view. Tests: `SkyRenderingTests` (storm draws two segments a drop; rain draws one; the
   existing pool test allows three draws a storm drop). *Frames:* `storm-summer-storm-town.png` before and after.

Considered and not changed: audio levels (section 2, needs ears); the unpainted-cell colour of the minimap (a
palette call for the design owner); the exit beacons (already labelled).

## 4. Grading against `docs/design/ui-standard.md`

- **§2 HUD regions:** unchanged. The minimap card keeps its fixed size and place (the top-right column above the
  menu column); no new HUD element.
- **§1 type and spacing:** unchanged; the change is colour and a weather layer.
- **§4 anchored to the world:** the storm and the tint are world-space, and no label moved.
- **§6 small screens (390×844):** the phone view is unchanged apart from the weather; the minimap stays hidden by
  default as before.
- **Overall:** pass on layout. The before/after frames show the three changes and no regressions in the HUD.

## 5. Open for the owner

- **Listening pass** on the class levels in [ADR-090](../adr/ADR-090-audio-mix-loudness-targets.md). The footstep is now
  about five decibels under the music (from about nine). This is the one item that needs ears.
- **Minimap void colour** (section 2): decide whether unpainted cells should read as "nothing" or as the map frame.
- **Exit beacons** are faint by design (30 % gold, 10 px). The label is the cue; whether the beacon should be stronger is a
  design call.

Closed in round 2 (section 6): fall, the deeper mine floors, the map-change fade, the exit signposts, snow and leaves.

## 6. Round 2

Captures and numbers: [`docs/qa/world-ambience-round2/`](../qa/world-ambience-round2/README.md). Changes:

- **Audio mix (ADR-090).** Every file measured (`tools/measure_audio.py`); one target level per class of sound; each file
  gets its gain once at load. Music −16 dBFS, actions and stingers −16, footsteps −18, UI −18, rain bed −16. Footsteps move
  from about nine decibels under the music to about five. Numbers: `docs/qa/world-ambience-round2/audio-levels.md`.
- **Hazard labels (ADR-091).** A magma field stacked "Magma fissure" on itself (`mine35`). One label of the same text now
  sits per 96 × 16 px. Frames: `before` and `after` `fall-noon-sun-mine35.png`.
- **Snow (ADR-027 amendment).** Near flakes are two pixels square; the one-pixel flakes were lost on grass and path.
- **Graded, not changed:** the fall farm (leaves read as leaves), the map-change fade (a dim frame of the old map at
  120 ms, then the new map), the farm exits (the label is the cue; the beacon is faint).
- **Coverage now:** fall (`fall-noon-sun`, Fall Day 5), mine floors 1, 6, 15, 25 and 35 (the four biomes), and the exits
  east, south and north of the farm. Still not covered: the other buildings, a full game day, touch play.
- **Listening** is still not possible here; the levels come from measurement.
