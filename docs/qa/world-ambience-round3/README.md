# World ambience, round 3

Evidence for the first open item of [`docs/design/critique-world-ambience.md`](../../design/critique-world-ambience.md) (section 6, the round-2
list): world labels at the bottom edge ran into each other and sat in the strip under the hotbar (mine floor 15), and the north exit label touched
the player's name tag. The rules are in [ADR-092](../../adr/ADR-092-world-labels-clear-the-hud-and-touch-controls.md).

Same method as rounds 1 and 2: a Development server with the calendar epoch set to the state, the tester client (`-p:NosumDevTools=true`,
`?qa=1`) in Chromium, English and Vietnamese, at 1280×720 and 390×844. `before` is master at `1a2119f` (PR #298); `after` is this branch.

## Frames

| Item | Frames (`before` and `after`, each with `en` and `vi`) |
|:--|:--|
| Bottom edge, hotbar (mine floor 15, Frost) | `{before,after}/{en,vi}/1280x720/fall-noon-sun-mine15.png`, and `390x844` |
| Touch controls at 390×844 (mine floor 15 and 35) | `{before,after}/{en,vi}/390x844/fall-noon-sun-mine15.png`, `fall-noon-sun-mine35.png` |
| Hazard labels (mine floor 35, Ruins) | `{before,after}/{en,vi}/1280x720/fall-noon-sun-mine35.png` (see "Open") |
| Magma floor (mine floor 25) | `{before,after}/{en,vi}/{1280x720,390x844}/fall-noon-sun-mine25.png` |
| North exit label over the name tag | `{before,after}/{en,vi}/1280x720/fall-noon-sun-exit-north.png` |
| Winter snow at 390×844 (farm, town) | `{before,after}/{en,vi}/390x844/winter-snow-{farm,town}.png` |

Each capture has a `-fade` frame 120 ms after a map change. Reports: `report-<scenario>-en+vi.json` (per frame: the HUD calendar text, the audio
buffers, and any page errors: none).

## What changed

1. **Bottom band and touch controls are HUD** (ADR-092 D1, D3). The bottom-centre region (toast, hotbar, gauges) is 172 px tall at 1280×720 and
   ends 12 px above the edge; a rectangle that close to the bottom is taken down to the edge. The touch stick and buttons join the HUD selector.
   A label that hits a rectangle moves beside it, then below, then above, then above the band; only then does it stay beside the rectangle.
   On `before/en/390x844/fall-noon-sun-mine15.png` the shrine and trap labels sat under the Chat, Run and Use buttons; on the `after` frame they
   are above the Chat button or beside the bar.
2. **Stacked labels keep a gap** (ADR-092 D4). "↑ Backwoods" touched the name tag at the farm's north exit
   (`before/en/1280x720/fall-noon-sun-exit-north.png`); with the gap they stack (`after/…`). A label at the top edge goes below the other one,
   not above it: lifting it off the canvas and clamping it back would have put it on top of the other label (found in this round's own capture,
   fixed before the final captures).
3. **Winter snow at 390×844.** Graded, not changed: the flakes read as snow on grass, the path and the town (`after/en/390x844/winter-snow-farm.png`).
   The HUD leaves them clear. Nothing in the frame needed a rule.
4. **Hazard labels at 390×844.** On the phone view, the magma labels sit at the right edge, clear of the touch buttons in the final frames
   (`after/en/390x844/fall-noon-sun-mine15.png`, "FLAME AURA" above the Chat button). In an earlier run of the same map, "SOLAR WAYSHRINE" touched
   "MAGMA FISSURE"; the stacking gap is the change for that, and it is covered by a unit test, not by a frame.

## Tests

- `tests/browser/label-fit/label-fit.test.mjs` (`node --test tests/browser/label-fit/`): 17 cases on the pure rules in
  `engine/Nosum.Rendering.Web/wwwroot/label-fit.js`: free spot, beside/below/above/band fallbacks, the touch stick, screen edges, the
  gap, stacking above and below, the step cap.
- `dotnet test tests/Nosum.UnitTests`: unchanged, passing (see the PR).
- Engine and wasm builds: 0 warnings, `-warnaserror`.

## Open

- **A stray "S" before some labels on mine floor 35** (`after/en/1280x720/fall-noon-sun-mine35.png`: "SFLAME AURA", "SVITALITY SHRINE";
  `before` has the same on "SCMAGMA FISSURE"). Its cause is not known. It is not a placement effect: it appears in the before
  frame too, on different labels. Not investigated in this round.

## Not changed, and why

- **The minimap's unpainted cells, the exit beacon's strength and listening levels** remain open for the owner (critique section 5). They are
  design or ears calls, not layout.

## Reproduce

```sh
# Postgres 16 on localhost:5432 (database nosum, role nosum); the client built with -p:NosumDevTools=true and served on :5080.
EPOCH=2026-10-02T10:43:39.000Z SCENARIO=fall-noon-sun OUT_DIR=docs/qa/world-ambience-round3/after LOCALES=en,vi VIEWPORTS=1280x720,390x844 \
  MAPS="mine15:5,5;mine35:5,5;mine25:5,5;farm:40,2:exit-north" SCRIPT=tests/browser/world-ambience-capture.cjs bash tests/browser/world-ambience-run.sh
EPOCH=2026-09-18T10:40:40.000Z SCENARIO=winter-snow OUT_DIR=docs/qa/world-ambience-round3/after LOCALES=en,vi VIEWPORTS=1280x720,390x844 \
  MAPS="farm:46,18;town:39,2" SCRIPT=tests/browser/world-ambience-capture.cjs bash tests/browser/world-ambience-run.sh
```

`before` is the same commands with `OUT_DIR=docs/qa/world-ambience-round3/before` on master at `1a2119f`.
