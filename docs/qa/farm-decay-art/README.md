# QA captures: thirsty marker (8 px water drop, top-right of the tile)

Change: commit `9562a08` on `claude/farm-decay-art`. A planted crop or bare tilled soil one dry game day from withering or
reverting shows an 8 x 8 water drop (`assets/sprites/drop.png`) in the top-right corner inside its tile, bobbing one pixel
every 600 ms. It replaces the full-size watering can of `6b318f6`. The morning toast ("N crops need water" /
"N cây cần tưới nước") is unchanged.

## Status: blocked. The served build cannot start the farm

**The client built from `9562a08` does not start the farm.** `drop.png` is not in the wasm project's `Content` list, so it is
not published to `wwwroot/assets/sprites/`. The decorator asks for it with the soil and crop textures, and the request returns
HTTP 404. The map fails to load and the game shows, in the active language:

- vi: "Không thể bắt đầu trò chơi: Error: texture assets/sprites/drop.png: HTTP 404"
- en: "Could not start the game: Error: texture assets/sprites/drop.png: HTTP 404"

Console: `Could not load map 'farm': Error: texture assets/sprites/drop.png: HTTP 404`. No tiles, crops or drops render, so
the required frames cannot be captured from the shipped build.

Cause: `wasm/Nosum.Wasm/Nosum.Wasm.csproj`, the `<Content Include=...>` list for `assets\sprites\...` (about line 20) names
`items.png`, `ambient.png`, ... `soil.png` but not `drop.png`. `GameMapsTests` checks hosted maps, not sprites, so nothing
caught it. Fix: add `..\..\assets\sprites\drop.png` to that list (the owner of `wasm/` makes this change; it was out of scope
for this QA task). Until then the drop does not appear in any build of `9562a08`.

## Files

| File | Shows |
|:--|:--|
| `farm-drop-blocked-<lang>-<WxH>.png` (x4) | The served `9562a08` build as shipped: the "could not start the game" screen. Evidence for the blocker. |
| `farm-drop-patched-<lang>-<WxH>.png` (x4) | Full frame from a **patched copy** (see below): thirsty crop and thirsty bare soil with the drop in the top-right corner. |
| `farm-drop-patched-close-<lang>-<WxH>.png` (x4) | Native-pixel crop of the tile strip from the patched copy. |
| `farm-drop-patched-close-<lang>-<WxH>-x4.png` (x4) | The same crop, nearest-neighbour x4. |
| `farm-drop-patched-farmer-over-drop-vi-1280x720-x4.png` | 4x crop with the farmer standing on thirsty tile B (the draw-order check). |
| `farm-drop-patched-toast-<lang>-1280x720.png` (x2) | The rollover frame with the morning toast, patched copy. |
| `farm-toast-<lang>-<WxH>.png` (x4) | Predates the drop change. Captured at `6b318f6` and shows the watering can marker. |
| `farm-withered-<lang>-<WxH>.png` (x4) | Predates the drop change. Captured at `6b318f6`. Tile A (withered) and B (thirsty soil), 4x. |

The superseded `farm-thirst-marker-*.png` files (watering can) were removed.

**Patched copy.** The patched frames come from the `9562a08` publish with one extra file: `assets/sprites/drop.png` copied into
`wwwroot/assets/sprites/` and served from a second static server. Nothing in the repo was changed. The served build therefore
differs from `9562a08` as shipped in exactly that one file. The patched frames show the drop art and its placement; they are
not evidence that the shipped build works.

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

1. PostgreSQL was running; the role `nosum` and the database `nosum_dev` already existed.
2. Server: `DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host` (WebSocket on 57300, "accepting connections").
3. Client: `dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumDevTools=true -o <scratchpad>/qa3/publish`
   from a clean `9562a08` tree (publish succeeded, exit 0), then `python3 -m http.server 5820 --directory <...>/publish/wwwroot`.
   Confirmed: `drop.png` absent from `publish/wwwroot/assets/sprites/` (`soil.png` present); `GET /assets/sprites/drop.png` = 404.
4. As-shipped screens (`asis.cjs`): new context with `deviceScaleFactor: 1`, `locale` vi-VN or en-US, `isMobile` and `hasTouch`
   for 390x844, and `localStorage['nosum.settings.v1'] = 'lang=1\n'` (vi) or `'lang=2\n'` (en). Open `/?qa=1`, click the guest
   button, wait 12 s, screenshot, record console errors.
5. Patched copy: the same publish with `drop.png` copied into `wwwroot/assets/sprites/`, served on 5821. The day schedule
   (`thirst-withdrop.cjs`) is the earlier helper's, with `BASE` set to 5821: tilling and planting with the mouse on the tile east
   of the farmer (`nosumQa.teleport`), tool keys 1 hoe, 2 can, 4 seeds, `nosumQa.advanceClock(1)` between days, each day gated on
   the first HUD weather icon reading sun. Viewports: 1280x720 (farmer's tile centre (638, 384), tile 48 px) and 390x844
   (centre (195, 438), tile 32 px).
6. Accepted patched runs: vi 1280x720, en 1280x720, vi 390x844, en 390x844. Each read sun on D+1, D+2 and D+3. No console or
   page errors were logged in these runs.
7. Draw-order check (`thirst-depth.cjs`, patched vi 1280x720): mouse moved off the strip, farmer teleported onto B (61, 28).
   Crop: `farm-drop-patched-farmer-over-drop-vi-1280x720-x4.png`.
8. Scripts and run logs are in the session scratchpad under `qa2/` (earlier helper) and `qa3/`; they are not part of the repo.

## Results (patched copy only)

**Visibility.** The drop is clear at native size and at 4x in both languages. The drop is a recognisable water drop, and the
art is the same in vi and en (no text in it).
- 1280x720: about 24 px on screen (8 px asset at the 3x world scale).
- 390x844: about 16 px on screen (8 px asset at the 2x world scale).

**Placement.** In both viewports and both languages the drop sits flush in the top-right corner of its tile, touching the
tile's right and top edges, and is not drawn on tiles A (withered) or H (healthy). Seen on B (bare soil), C (grown crop)
and E (seed).

**Draw order.** With the farmer standing on thirsty tile B, the farmer's shirt covers the left edge of B's drop, so the
farmer is drawn over the drop. The farmer standing one row below the strip did not overlap any drop, so that case is not
tested. Evidence for the rule "drawn under players lower on the screen" is therefore partial: one overlap, which passes.

**Observation.** The target-tile highlight (the green-framed tile in front of the farmer, or under the hover point) is drawn
over the drop. When the farmer faces a thirsty tile, its highlight hides most of that tile's drop. This is cosmetic, not a
blocker, and the drop reappears when the farmer moves.

**Toast.** Read correctly in both languages (unchanged by this commit):

| Run | Toast |
|:--|:--|
| vi 1280x720 | "2 cây cần tưới nước" |
| en 1280x720 | "2 crops need water" |
| vi 390x844 | "2 cây cần tưới nước" |
| en 390x844 | "2 crops need water" |

## Defects and notes

- **Blocker: `drop.png` not published.** See Status. Every build of `9562a08` shows the "could not start the game" screen
  instead of the farm, in both languages and both viewports. Fix: add `drop.png` to the wasm project's sprite `Content` list.
- **No gate for the sprite list.** `GameMapsTests` checks map hosting but nothing checks that every sprite the client loads is
  published, which is how this slipped through.
- **Singular toast not captured.** The one-crop text is in the resources, but no accepted patched run had exactly one thirsty
  crop.
- **Withered sprite hard to read.** Dark olive on the dark bed (from the earlier capture). Not changed by this commit.
- **Target highlight covers the drop** when the farmer faces a thirsty tile (see Results). Cosmetic.
