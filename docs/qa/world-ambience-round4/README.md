# World ambience, round 4

Evidence for round 4 of [`docs/design/critique-world-ambience.md`](../../design/critique-world-ambience.md) (section 8). Three items:

1. **The stray letter before some mine floor 35 labels** (the open item of round 3). Fixed in the placement order (ADR-095 D6). Frames: `before/` and
   `after/` (`en` and `vi`, `1280x720`, `fall-noon-sun-mine35.png`). Proof is the unit reproduction, not the frame: see the limits below.
2. **Audio listening proxy** (ADR-090 amendment). Footsteps move from −18.0 to −16.5 dBFS target; the numbers and the method are in
   [`audio-proxy.md`](audio-proxy.md). The script is `tools/listening_proxy.py`.
3. **Next open item.** The critique's remaining items are owner calls, not layout bugs: the minimap's unpainted cells (a palette decision) and the exit
   beacon's strength (a design decision). Not changed in this round; section 8 of the critique lists them for the owner.

## Frames

`before` is master at `2311270` (PR #305); `after` is this branch. Same Development server and calendar state for both
(`EPOCH=2026-10-02T10:43:39.000Z`, `fall-noon-sun`), client built with `-p:NosumDevTools=true`, Chromium, `1280x720`, map `mine35`, tile `5,5`.

| Frame | Before | After |
|:--|:--|:--|
| English | `before/en/1280x720/fall-noon-sun-mine35.png` | `after/en/1280x720/fall-noon-sun-mine35.png` |
| Vietnamese | `before/vi/1280x720/fall-noon-sun-mine35.png` | `after/vi/1280x720/fall-noon-sun-mine35.png` |
| After a map change (fade) | `before/*/1280x720/fall-noon-sun-mine35-fade.png` | `after/*/1280x720/fall-noon-sun-mine35-fade.png` |

Reports: `report-fall-noon-sun-en+vi.json` in each folder (the HUD calendar text, the audio buffers and page errors per frame).

## Limits, stated plainly

- The two captures are separate runs: the clock reads 5:12 PM in `before` and 5:05 PM in `after`, and the player stands on a different tile
  (the walk arrival rule). The labels that exist differ between the two frames, so the frames cannot show the same label side by side. The
  "FLAME AURA" label of round 3 is not in either round-4 frame.
- The stray letter was not reproduced in a live frame this round. The cause is shown by a reproduction in node
  (`tests/browser/label-fit/label-fit.test.mjs`, `placeLabel_HudFitOntoPlacedLabel_StaysClearOfIt`): a label moved beside a HUD rectangle lands on
  a label already drawn, and the one underneath shows at the edge. That is the only placement path found that does this; it is the likely cause.
- The listening levels are a measured proxy. A person still has to listen.

## Tests

- `node --test tests/browser/label-fit/label-fit.test.mjs`: 20 cases (17 from round 3, 3 new for `placeLabel`).
- `dotnet test tests/Nosum.UnitTests`: the audio mix tests (`AudioCatalogTests`) with the footstep target at −16.5.
- Engine and wasm builds: 0 warnings, `-warnaserror`.

## Reproduce

```sh
# Postgres 16 on localhost:5432 (database nosum, role nosum); the client built with -p:NosumDevTools=true and served on :5080.
dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumDevTools=true -o out/qa
node bench/wasm-perf/serve.cjs out/qa/wwwroot 5080 &
EPOCH=2026-10-02T10:43:39.000Z SCENARIO=fall-noon-sun OUT_DIR=docs/qa/world-ambience-round4/after LOCALES=en,vi VIEWPORTS=1280x720 \
  MAPS="mine35:5,5" SCRIPT=tests/browser/world-ambience-capture.cjs bash tests/browser/world-ambience-run.sh
```

`before` is the same with `OUT_DIR=docs/qa/world-ambience-round4/before` on master at `2311270`.
