# World ambience, round 5

Evidence for round 5 of [`docs/design/critique-world-ambience.md`](../../design/critique-world-ambience.md) (section 9). Rule: [ADR-098](../../adr/ADR-098-label-scope-and-the-round-five-check.md).

## Frames

| Folder | Build | What it shows |
|:--|:--|:--|
| `master-309/` | master at `d6642a4` (PR #309 merged) | Mine floor 35 (`mine35`, tile 5,5) and a farm frame, English and Vietnamese. Full view and a x4 crop of its centre. |
| `round5/` | this branch (#309 plus the ADR-098 renderer fix) | The same mine floor 35 frame, English and Vietnamese. |

Each frame has `<tag>.png` (the whole view, 1280 × 720) and `<tag>-centre-4x.png` (the middle 320 × 180 px scaled 4× with nearest-neighbour, so pixels stay hard).
Script: `tests/browser/world-ambience-label-zoom.cjs`. The calendar text the HUD showed is printed by the script (`Fall Day 5`, 5:26–5:32 PM).

## What the frames show

- **No stray letter** in any mine floor 35 frame taken, English or Vietnamese, on either build. That is absence in a few frames. The letter depends on which labels
  overlap at one moment, and no before build (master before #309) was captured at the same clock and tile, so this does not confirm #309's fix.
- The labels on `mine35` ("SOLAR WAYSHRINE", "MAGMA FISSURE", "VITALITY SHRINE", "↑ Way Up · [E] Home") sit clear of the HUD and of each other.
- Vietnamese: two monsters of the same name stack their labels ("HUYẾT THỆ" twice). Two monsters, two labels.

## Limits

- One script run per frame; the player's tile and the clock differ between frames.
- No frame had a quest waypoint off the view on its map, so the quest arrow (ADR-096) was not in any frame. The `renderer.js` fault that round 5 fixed
  (an unimported `LABEL_EDGE` in the arrow's placement) is shown by reading the code and by `renderer-scope.test.mjs`, not by a frame.
- Audio was not re-measured in this round.

## Tests

- `node --test tests/browser/label-fit/label-fit.test.mjs` (20 cases) and `node --test tests/browser/label-fit/renderer-scope.test.mjs` (4 cases; two of them fail on
  the `renderer.js` before this round).
- `dotnet test tests/Nosum.UnitTests` and the engine and wasm builds with `TreatWarningsAsErrors` (see the PR).

## Reproduce

```sh
# Postgres 16 on localhost:5432 (database nosum, role nosum); the client built with -p:NosumDevTools=true and served on :5080.
dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumDevTools=true -o out/qa
node bench/wasm-perf/serve.cjs out/qa/wwwroot 5080 &
ASPNETCORE_ENVIRONMENT=Development NOSUM_CALENDAR_EPOCH=2026-10-02T10:43:39.000Z dotnet run --no-build --project server/Nosum.Host &
MAP=mine35 TILE=5,5 OUT_DIR=docs/qa/world-ambience-round5/round5 TAG=mine35-en node tests/browser/world-ambience-label-zoom.cjs
MAP=mine35 TILE=5,5 OUT_DIR=docs/qa/world-ambience-round5/round5 TAG=mine35-vi LOCALE=vi-VN node tests/browser/world-ambience-label-zoom.cjs
```
