# QA captures: thirsty marker (watering can icon above thirsty tiles)

Change: commit `6b318f6` on `claude/farm-decay-art`. A planted crop or bare tilled soil one dry game day from withering or
reverting now shows the watering can icon (from `tools.png`, no new art) above its tile, in addition to the parched-yellow
tint. The morning toast "N crops need water" (en) / "N cây cần tưới nước" (vi) appears at the day rollover when any crop is
at risk.

Captured with the Development server (`DOTNET_ENVIRONMENT=Development`, database `nosum_dev`) and the client built from
`6b318f6` with `-p:NosumDevTools=true`, as a guest on the farm map. Languages: vi = `lang=1` (`GameLanguage.Vietnamese`),
en = `lang=2`. Viewports: 1280x720 (mouse) and 390x844 (phone, touch emulation). Each run is one language and one viewport.

## Files

| File | Shows |
|:--|:--|
| `farm-thirst-marker-<lang>-<WxH>.png` (x4) | Full frame. Thirsty crop C and thirsty bare soil B (and thirsty seed E) in the seven-tile strip; the watering can marker above each. |
| `farm-thirst-marker-close-<lang>-<WxH>.png` (x4) | Native-pixel crop of the strip (tiles A to G, row 28, plus the space above). |
| `farm-thirst-marker-close-<lang>-<WxH>-x4.png` (x4) | The same crop, nearest-neighbour x4. |
| `farm-toast-<lang>-<WxH>.png` (x4) | The frame at the rollover to D+3 with the morning toast. |
| `farm-withered-<lang>-<WxH>.png` (x4) | Nearest-neighbour x4 of tiles A (withered) and B (thirsty soil), the focus crop of the withered tile. Same moment as the other frames. |

Tiles on the row (farm map row 28, left to right): A (x 60), B (x 61), C (x 62), E (x 63), H (x 64), K (x 65), G (x 66).
All crops are radish (seed 101, hotbar slot 4).

| Tile | How it was made | State at the rollover to D+3 |
|:--|:--|:--|
| A | Tilled and planted on D. Never watered. | Withered (two dry days). No marker: it is already withered. |
| B | Tilled on D, never planted or watered. | Thirsty bare soil (two dry days). Marker shown. |
| C | Tilled and planted on D. Watered on D and D+1, dry on D+2. | Thirsty, grown crop (one dry day). Marker shown. |
| E | Tilled and planted on D+1. Never watered. | Thirsty, stage-0 seed (one dry day). Marker shown. |
| H | Tilled and planted on D. Watered on D, D+1 and D+2. | Healthy control. No marker. |
| K, G | Tilled (K) and tilled and planted (G) after the toast, on D+3. | Fresh controls. No marker. |

## Steps used

1. PostgreSQL was running; the role `nosum` and the database `nosum_dev` already existed.
2. Server: `DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host` (WebSocket on 57300).
3. Client: `dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumDevTools=true -o <scratchpad>/publish2`,
   then `python3 -m http.server 5820 --directory <scratchpad>/publish2/wwwroot`.
4. Per run: a new context with `deviceScaleFactor: 1`, `locale` (vi-VN or en-US), `isMobile` and `hasTouch` for 390x844,
   and `localStorage['nosum.settings.v1'] = 'lang=1\n'` (vi) or `'lang=2\n'` (en). Open `http://localhost:5820/?qa=1`,
   click the guest button (`/khách|guest/i`), wait for the toolbar, wait 3 s.
5. Grants with `nosumQa.grant`: hoe (1, 1), watering can (2, 1), radish seeds (101, 10).
6. Tilling and planting with the mouse on the tile east of the farmer (`nosumQa.teleport('farm', x-1, 28)`), tool keys
   1 hoe, 2 can, 4 seeds. Farmer's tile centre on screen: 1280x720 (638, 384), tile 48 px; 390x844 (195, 438), tile 32 px.
7. Days: `nosumQa.advanceClock(1)`, each advance gated on the first HUD weather icon reading sun (rain would water the
   outdoor tiles and break the dry run). Runs with rain on D+1, D+2 or D+3 were aborted and repeated on a later game day.
8. Schedule: D (till A, B, C, H; plant A, C, H; water C, H) -> advance -> D+1 (water C, H; till and plant E) -> advance ->
   D+2 (water H) -> advance -> D+3 rollover. The toast is polled for `/need water|cần tưới nước/i` immediately after the
   advance and screenshotted as soon as it shows. Then K and G are made as controls, the farmer is teleported to (61, 27),
   and the frame is taken 1.8 s later.
9. Accepted runs: vi 1280x720 (Spring Day 24, 11:17 AM), en 1280x720 (Spring Day 16, 11:11 AM), vi 390x844 (Summer Day 4),
   en 390x844 (Summer Day 1). Each gate read sun on D+1, D+2 and D+3. No page or console errors were logged in any accepted run.
10. The scripts (`thirst.cjs`, `gate.py`) and run logs are in the session scratchpad under `qa2/`; they are not part of the repo.

Deviation from the earlier capture: the earlier README used one shared viewport set per attempt and aborted runs on rain. This
capture runs one language and one viewport per run, each with its own page and clock.

## Results

**Toast.** Read correctly in both languages and both viewports:

| Run | Toast | Time after the advance |
|:--|:--|:--|
| vi 1280x720 | "2 cây cần tưới nước" | 73 ms |
| en 1280x720 | "2 crops need water" | 81 ms |
| vi 390x844 | "2 cây cần tưới nước" | 650 ms |
| en 390x844 | "2 crops need water" | 918 ms |

The toast sits centred above the hotbar at 1280x720 and left of the Use button at 390x844, without overlapping either.
The Vietnamese line fits on one line at both sizes.

**Marker.** Visible in every full frame and at native size in the close crops:
- Above B (bare soil): the can sits just above the soil tile, beside the farmer's feet. Clear.
- Above C (grown, thirsty crop): the can sits up and to the right of the sprout. Clear at native size.
- Above E (stage-0 seed): a can above an otherwise plain dark seed tile. Clear.
- Not shown above A (withered) or H (healthy watered). The marker is only for thirsty tiles, as intended.

At 1280x720 the marker is about 40 px on screen (a 48 px tile), and at 390x844 about 32 px (a 32 px tile). The icon is the
same can as the hotbar slot 2 icon, so it reads as "water this" without needing the tint. Judged at native size: legible.

**Withered (A).** A is a dark olive sprout on a dark tilled bed. It is distinguishable from the healthy tiles only by its
darker, duller colour; the leaves are not brown. The brown withered art is not in this commit, so it is not expected to
look different yet. The marker is not drawn on it, so the only cue at a glance is the sprout's colour.

## UI standard check (docs/design/ui-standard.md, section 7)

| # | Item | Result | Note |
|:--|:--|:--|:--|
| 1 | Element sits in its region | Pass | Marker is on the tile; toast in the toast line above the hotbar. |
| 2 | Nothing overlaps | Pass | Toast clear of the hotbar (1280) and the Use button (390). |
| 6 | Three font sizes, no wrap, nothing clipped | Pass | Vietnamese and English toasts on one line. |
| 7 | No inner scrollbar at 1280x720 and up | Pass | None visible. |
| 10 | Vietnamese and English both fit | Pass | Both captured at both viewports. |

Other items (menu column, panels, spacing) are not affected by this change.

## Defects and notes

- **Singular form not captured.** The one-crop text ("1 crop needs water" / "1 cây cần tưới nước") is in the resources
  (`Farm_Thirsty_Morning_One` in `Language.en.resx` and `Language.resx`), but no accepted run had exactly one thirsty crop,
  so the singular toast is not in these captures.
- **Withered sprite is hard to read.** It is dark olive on the dark bed (see Results). Not changed by this commit; the
  brown art is the follow-up the brief says is not included.
- **Marker over a stage-0 seed is small.** At native size E's can is clear, but the seed sprite under it is a few dots, so
  the marker is the only cue that E is thirsty.
