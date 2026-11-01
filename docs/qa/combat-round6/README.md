# Combat round 6: strike cue at phone width, and the tint guard

Tester build (`dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`), Development server, page `?qa=1`. Capture: `docs/qa/combat-round5/capture.cjs telegraph`
at 390x844 (`TG_DX=0 TG_DY=1`, the farmer one tile below an ordinary monster). Frames run from about 800 ms after the first landing; the second strike's wind-up
sits at about +1100..+1500 ms.

## 1. The strike cue at 390x844

What was checked:

| Run | Locale | Cue in wind-up frames | Notes |
| :-- | :-- | :-- | :-- |
| Round-5 capture, `after/tg-390x844-vi` | vi | not seen in tg-07 / tg-08 | Round 5 did not explain this. |
| This round, before the tint migration (scratchpad run) | vi | **visible** from about 1082 to 1424 ms, above the farmer's name plate | Contact sheet of the farmer region, 13 frames. |
| This round, before the tint migration (scratchpad run) | en | **visible** in the full frame at 1227 ms, above the farmer's head | Also a second cue over another monster nearby. |
| `after/r6-390-vi` (after the migration) | vi | **visible** from about 1130 to 1413 ms | `sheet-r6-390-vi.png` |
| `after/r6-390-en` (after the migration) | en | **not visible** | The farmer has no monster beside it in the frame; the hits (84 to 76 to 68) land while the nearest monster is about 200 px away. **Unexplained.** |

What this shows:

- The cue draws at 390x844 in both locales in several runs, with the same code path as at 1280x720 (`MonstersDecorator` overlay, `Flush`/`FlushLabels` culling, renderer `-6`). Viewport culling, zoom and render scale are not the cause in the runs I checked: at 390 wide the zoom is 2 and the cue is 6 px wide, and it still draws.
- The round-5 negative frames and the last English run have one thing in common: the farmer and the striking monster are not visibly adjacent in the frame. The hits still land, so the server and the client disagree about where the farmer or the monster is on screen. That points at the **wrong monster or wrong position in frame**, not at the cue drawing. It is not proven.
- Not established: why the farmer is drawn away from the monster that hits it in the English run. The next step is to log the farmer's tile and the striking monster's tile from the client (`window.nosumQa`) on every frame of the wind-up.

No change to the cue's drawing was made in this round. The colours were already `0xRRGGBBAA` on master (round 5). The cue draws at 390x844 in vi after the migration and in both locales before it; the English run after the migration is the open item above.

## 2. Tint guard

- `engine/Nosum.Rendering/Rgba.cs`: `Rgba.From(r, g, b, a)` packs `0xRRGGBBAA` (channels clamped), `Rgba.WithAlpha`.
- Every tint and fill literal on a line that mentions `tint`, `FillTexture`, `AddOverlay` or `FillRect` in `wasm/` and `engine/` now uses `Rgba.From(...)`. 70 literals in 13 files; the byte values are unchanged.
- Two defaults were not literals to begin with: `FarmerLook` defaults use `RenderCommandBuffer.NoTint` (the same all-ones bits as opaque white, and a constant, so it works as a default parameter); the wardrobe "no colour" sentinel uses `-1`.
- `tests/Nosum.UnitTests/Engine/RgbaTests.cs`: the byte order and clamping of `Rgba`, and a source scan that fails on any raw 8-digit hex literal on a tint or fill line in `wasm/` and `engine/`.

Known gap of the scan: a literal on a line that names no tint or fill (for example a `switch` arm in `MonstersDecorator`, `auraTint`) is not checked. Those values are already `0xRRGGBBAA`, and the rule the scan can see is the one that caused PR #322.

## Results

- `dotnet build wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`: 0 warnings, 0 errors.
- `dotnet test tests/Nosum.UnitTests`: 3694 passed, 0 failed, 0 warnings.

## Captures

- `after/r6-390-vi/`: telegraph frames at 390x844, vi, after the migration (`tg-*.png`, `result.json`).
- `after/r6-390-en/`: telegraph frames at 390x844, en, after the migration.
- `sheet-r6-390-vi.png`, `sheet-r6-390-en.png`: the farmer region across the frames (120..260 x 180..340, 3x).
