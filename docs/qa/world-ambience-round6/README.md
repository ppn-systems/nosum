# World ambience, round 6

Evidence for round 6 of [`docs/design/critique-world-ambience.md`](../../design/critique-world-ambience.md) (section 10). Rule: [ADR-110](../../adr/ADR-110-elite-badges-do-not-stack.md).

## Frames

`after/` is the build with the elite-badge rule (this branch, rebased on master at `1e12a57b`). One calendar state, noon spring sun
(`EPOCH=2026-10-30T10:40:40.000Z`, shown in the frames as Spring Day 7, 6:57 to 7:03 AM), five maps, English and Vietnamese, at 1280 × 720, 1920 × 1080 and 390 × 844.

| Path | What it shows |
|:--|:--|
| `after/<locale>/<viewport>/noon-spring-sun-<map>.png` | The whole view on that map (farm, town, beach, forest, mine1) |
| `after/<locale>/<viewport>/noon-spring-sun-<map>-fade.png` | The same map during its fade-in |
| `report-noon-spring-sun-en+vi.json` | One entry per frame: locale, viewport, map, the calendar text the HUD showed, and the audio buffers started in the visit |

## What the frames show

- The ordinary views at all three sizes and both languages, with the HUD clear of the labels and the phone's touch controls readable. The layout is
  unchanged from master apart from the rebase.
- **No elite pack is in these frames.** No QA path spawns elites, so the badge rule is not seen in a frame. It is proven by
  `tests/Nosum.UnitTests/Wasm/PlacedLabelSetTests.cs` (six cases) and by the hazard rule's tests, which run unchanged on the shared class.

## Limits

- One calendar state. The round 6 change is not time-of-day dependent.
- No before frames for this change: the change draws nothing different in an ordinary view, so the before/after pair would not differ.
- The capture script was fixed in this round: every default map wrote the same `…-undefined.png` file, so only the last map survived. A default map is now
  named by its id (`tests/browser/world-ambience-capture.cjs`).

## Reproduce

```sh
# Postgres 16 on localhost:5432 (database nosum, role nosum); the client published with -p:NosumDevTools=true and served on :5080.
dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumDevTools=true -o artifacts/qa-after
node bench/wasm-perf/serve.cjs artifacts/qa-after/wwwroot 5080 &
EPOCH=2026-10-30T10:40:40.000Z SCENARIO=noon-spring-sun OUT_DIR=docs/qa/world-ambience-round6/after LOCALES=en,vi \
  VIEWPORTS=1280x720,1920x1080,390x844 tests/browser/world-ambience-run.sh
```
