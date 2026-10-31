# QA captures: thirsty marker (8 px water drop, top-right of the tile)

Change: `9562a08` (the drop) and `b454a3f` (publishes `drop.png`), branch `claude/farm-decay-art`. A planted crop or bare
tilled soil one dry game day from withering or reverting shows an 8 x 8 water drop (`assets/sprites/drop.png`) in the
top-right corner inside its tile, bobbing one pixel every 600 ms. The morning toast ("N crops need water" /
"N cây cần tưới nước") is unchanged.

## Status: captured from the real published client at `b454a3f`

The captures below come from a Release publish of `b454a3f` built with the QA hooks (`-p:NosumDevTools=true`), with no
patched files. `drop.png` is in `wwwroot/assets/sprites/` and the farm starts in both languages with no console or page
errors in the accepted runs. The drop is visible at native size and at 4x in vi and en, at both viewports.

## Files

| File | Shows |
|:--|:--|
| `farm-drop-<lang>-1280x720.png`, `farm-drop-<lang>-390x844.png` | Full frame at the 1280x720 and 390x844 viewports (4 files). Thirsty crop B and thirsty bare soil C/E with their drops. Farmer on tile 61 one row above the strip. |
| `farm-drop-close-<lang>-<WxH>.png` | Native-pixel crop of the tile strip (row 28, tiles A to G) with the drops (4 files). |
| `farm-drop-close-<lang>-<WxH>-x4.png` | The same crop, nearest-neighbour x4 (4 files). |
| `farm-drop-below-close-<lang>-<WxH>-x4.png` | Extra. Same strip at 4x from a frame where the farmer stands one row **below** the strip (row 29, tile column 62). Draw-order check (4 files). |
| `farm-drop-toast-<lang>-1280x720.png` | Morning toast on the rollover frame (2 files). |

Both viewports use the same schedule and tiles. Tile positions in the 1280x720 full frame: strip at about x 568 to 904,
y 410 to 460 (48 px tiles). In the 390x844 full frame: strip at about x 146 to 370, y 456 to 488 (32 px tiles).

Tiles on the row (farm map row 28, left to right): A (x 60), B (x 61), C (x 62), E (x 63), H (x 64), K (x 65), G (x 66).
All crops are radish (seed 101, hotbar slot 4).

| Tile | How it was made | State at the rollover to D+3 |
|:--|:--|:--|
| A | Tilled and planted on D. Never watered. | Withered (two dry days). No drop. |
| B | Tilled on D, never planted or watered. | Thirsty bare soil (two dry days). Drop shown. |
| C | Tilled and planted on D. Watered on D and D+1, dry on D+2. | Thirsty, grown crop (one dry day). Drop shown. |
| E | Tilled and planted on D+1. Never watered. | Thirsty, stage-0 seed (one dry day). Drop shown. |
| H | Tilled and planted on D. Watered on D, D+1 and D+2. | Healthy control. No drop. |
| K, G | Tilled (K) and tilled and planted (G) after the toast, on D+3. | Fresh controls. No drop. |

## Steps used

1. PostgreSQL was running; role `nosum` and database `nosum_dev` already existed.
2. Client publish (QA hooks on, into the session scratchpad):
   `dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumDevTools=true --no-restore -o <scratchpad>/pub-qa`
   (`DOTNET_ROOT=/root/.dotnet`, `PATH=/root/.dotnet:$PATH`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`). Exit 0.
   `wwwroot/assets/sprites/drop.png` present. The page's `<base href="/">` matches serving from `wwwroot` at the root.
3. Static client: `python3 -m http.server 5820 --bind 127.0.0.1` in `<scratchpad>/pub-qa/wwwroot` (`BASE_URL` http://localhost:5820).
4. Server: `DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host --no-restore`, WebSocket on port 57300
   ("The server is accepting connections", ws_port 57300). Two sign-in notices (Google and Microsoft client IDs not set) are
   expected and harmless for guest play.
5. Language: `localStorage['nosum.settings.v1']` = `lang=1\n` for vi, `lang=2\n` for en (`GameLanguage.Vietnamese = 1`,
   `English = 2`; `ClientSettings` reads `lang`).
6. Schedule, driven with `window.nosumQa` (`?qa=1`, `teleport`, `grant`, `advanceClock`), by the capture script
   `thirst.cjs` in the scratchpad (`qa4/`): day D tills A, B, C, H and plants A, C, H, and waters C, H; `advanceClock(1)`
   per day; D+1 waters C, H and tills and plants E; D+2 waters H; `advanceClock(1)` to D+3 and polls for the toast every
   50 ms; then the controls K and G, a frame with the farmer on tile (61, 27), and a frame with the farmer on (62, 29).
   Each day is gated on the first HUD weather icon reading sun (`gate.py`); a rain day aborts the run, so each config was
   retried (up to six tries).
7. Runs accepted (all sun on D+1 to D+3, `errors` empty in the run log):

| Config | Accepted run | Toast read | Toast timing after rollover |
|:--|:--|:--|:--|
| vi 1280x720 | try 2 | "2 cây cần tưới nước" | 405 ms |
| en 1280x720 | try 4 | "2 crops need water" | 857 ms |
| vi 390x844 | try 2 | "2 cây cần tưới nước" | 1005 ms |
| en 390x844 | try 1 | "2 crops need water" | 349 ms |

Rain aborted earlier tries (D+1, D+2 or D+3); those runs are not used.

## Results

**Visibility.** The drop is clear at native size and at 4x in both languages and both viewports. It is a recognisable water
drop with no text, so the art is identical in vi and en.
- 1280x720: about 24 px on screen (8 px asset at the 3x world scale).
- 390x844: about 16 px on screen (8 px asset at the 2x world scale).

**Placement.** The drop sits flush in the top-right corner of its tile, touching the tile's right edge. It is drawn on B
(bare soil), C (grown crop) and E (seed), and is absent on A (withered) and H (healthy). The same holds at both viewports
and in both languages.

**Draw order.** With the farmer on row 29 (one row below the strip), the farmer's head covers the lower part of C's drop
at 1280x720 vi (`farm-drop-below-close-vi-1280x720-x4.png`). The drop is therefore under a player drawn lower on the
screen, which is the intended rule. At 390x844 vi the same overlap occurs: the head covers the lower part of C's drop
(`farm-drop-below-close-vi-390x844-x4.png`).

**Toast.** Read in both languages and both viewports, appears 349 to 1005 ms after the rollover, and the drops stay visible
in the same frame (`farm-drop-toast-*`).

## Defects and notes

- **Target highlight covers the drop.** When the farmer faces a thirsty tile, the green-framed target highlight is drawn
  over that tile's drop (seen on B in `farm-drop-close-vi-1280x720-x4.png`). Cosmetic. The drop reappears when the farmer moves.
- **Singular toast not captured.** The one-crop text is in the resources, but no accepted run had exactly one thirsty crop.
- **Bobbing not captured.** The captures are single frames; the 600 ms bob is not shown in them.
- **Draw order tested with one overlap case.** Only the row-29 overlap on C was checked (one player, one tile).
- **Gate for the sprite list.** `GameMapsTests` checks map hosting, not sprites. Nothing guards that every sprite the client
  loads is published, which is how `drop.png` was missed in `9562a08`. Consider a test that compares the sprite list with `Content`.
