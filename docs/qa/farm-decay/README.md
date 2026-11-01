# QA captures: farm decay (thirsty crops, withered crops, thirsty warning)

Change: commit `22bce3c` on `claude/farm-decay-clarity` (planted crops and bare soil one dry game day from withering or
reverting are drawn thirsty; a withered crop is drawn brown; the morning message line says "N crops need water").

Captured with the Development server (`DOTNET_ENVIRONMENT=Development`, database `nosum_dev`) and the client built with
`-p:NosumDevTools=true`, opened at `/?qa=1` as a guest, English (`lang=2`), on the farm map. Viewports: 1280x720, 1920x1080
and 390x844 (phone, touch emulation). The final frames are at 10 AM game time (daylight).

## Files

| File | Scene | Shows |
|:--|:--|:--|
| `thirsty-toast-<WxH>.png` (x3) | 1. thirsty-toast | The frame at the rollover to day D+3 with the message line "2 crops need water" above the hotbar. |
| `thirsty-crop-and-soil-<WxH>.png` (x3) | 2. thirsty crop and soil | Full frame taken about 12 s after the rollover, after the toast has gone and the two control tiles were made. Thirsty crop C and thirsty bare soil B are in the seven-tile strip in the middle. |
| `withered-crop-<WxH>.png` (x3) | 3. withered crop | The same frame as scene 2 (scenes 2, 3 and 4 are one moment, so the files are identical). Withered crop A. |
| `watered-control-<WxH>.png` (x3) | 4. watered control | The same frame. Healthy watered crop H beside the others. |
| `strip-native-<WxH>.png` (x3) | 2, 3, 4 | The seven-tile strip at native pixels. |
| `strip-zoom4-<WxH>.png` (x3) | 2, 3, 4 | The same strip, nearest-neighbour x4. |
| `defect-singular-toast-1280x720.png` | defect | An earlier 1280 run (Spring Day 16) with one thirsty crop: the toast reads "1 crops need water". |

Tiles on the row (left to right, farm map row 28, the tillable strip): A (x 60), B (x 61), C (x 62), E (x 63), H (x 64),
K (x 65), G (x 66). The farmer stands on row 27 above B. Every crop is a radish (`ItemCatalog` seed 101, hotbar slot 4).

| Tile | How it was made | State at the rollover to D+3 |
|:--|:--|:--|
| A | Tilled and planted on D. Never watered. | Withered (two dry days: D+1, D+2). Not counted in the message. |
| B | Tilled on D, never planted or watered. | Thirsty bare soil (two dry days). Not counted: the message counts crops only. |
| C | Tilled and planted on D. Watered on D and D+1, dry on D+2. | Thirsty, grown (one dry day). Counted. |
| E | Tilled and planted on D+1. Never watered. | Thirsty, stage 0 (one dry day). Counted. |
| H | Tilled and planted on D. Watered on D, D+1 and D+2. | Healthy control. |
| K | Tilled after the toast, on D+3. | Fresh soil control (not thirsty). |
| G | Tilled and planted after the toast, on D+3. | Fresh seed control (not thirsty). |

## Steps used

1. PostgreSQL was running. The role `nosum` existed. The database `nosum_dev` was missing (the server failed with
   `3D000 database "nosum_dev" does not exist`), so it was created owned by `nosum`.
2. Server: `DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host` (WebSocket on 57300).
3. Client: `dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumDevTools=true -o <scratchpad>/publish`,
   then `python3 -m http.server 5820 --directory <scratchpad>/publish/wwwroot`. `assets/farmer/boots.png` is in the build,
   so the boots workaround in `docs/qa/boots/README.md` was not needed.
4. Playwright (preinstalled Chromium, no `playwright install`): a new context per viewport with `deviceScaleFactor: 1`,
   `locale: en-US`, `isMobile` and `hasTouch` for 390x844, and `localStorage['nosum.settings.v1'] = 'lang=2\n'`. Open
   `http://localhost:5820/?qa=1`, click "as a guest", wait for the toolbar, wait 3 s.
5. Grants with `nosumQa.grant`: hoe (1, 1 piece), watering can (2, 1), radish seeds (101, 10).
6. Tilling and planting use the mouse on the tile east of the farmer: `nosumQa.teleport('farm', x-1, 28)`, then the
   mouse moves to the centre of the tile east of the farmer and is held for 350 ms (tool keys: 1 hoe, 2 can, 4 seeds).
   Screen offsets of the farmer's tile centre: 1280x720 (638, 384) with one tile 48 px; 1920x1080 (960, 580) with
   80 px; 390x844 (195, 438) with 32 px (the tile size follows `ViewZoom.Pick`).
7. Days: `nosumQa.advanceClock(1)` (the caller's page). Each advance is gated: the HUD's first weather icon must show sun
   on D+1, D+2 and D+3 (rain waters outdoor tiles, so a rainy day would break the dry run). Attempts with rain were
   aborted and repeated on a later day.
8. Schedule: day D (till A, B, C, H; plant A, C, H; water C, H) -> advance -> D+1 (water C, H; till and plant E) ->
   advance -> D+2 (water H) -> advance -> D+3 (rollover). The toast is polled for `/need water/i` and screenshotted as
   soon as it shows. Wait 3.5 s, then K (till) and G (till and plant) on D+3 as controls, then teleport the farmer to
   (61, 27), wait 1.8 s and take the full frame.
9. Final runs (each viewport on its own run, its own page advancing the clock): 1280x720 attempt 60 (Summer Day 10,
   10:20 AM), 1920x1080 attempt 61 (Fall Day 2, 10:33 AM), 390x844 attempt 71 (Fall Day 22, 10:47 AM). All three gates
   read sun on D+1, D+2 and D+3. No page or console errors were logged in any of the three runs.
10. The scripts are in the session scratchpad (`qa/attempt.cjs`, `qa/gate.py`); they are not part of the repo.

Deviations from the suggested recipe: the recipe's day counts did not match the rules in `FarmRules` (a crop planted on day D
gets a grace day, so it is thirsty after two game days and withered after three; bare soil is thirsty after two dry days
and reverts after three). The schedule above follows the rules. The dev server was restarted once: the season skips had set the clock to midnight (dark frames), and a restart returned the clock to real time.

## Scene results

**1. thirsty-toast (all three viewports):** the message line reads "2 crops need water" (C and E). Placed above the hotbar,
centred, and it did not overlap the hotbar, the HUD or the phone's Use button. The toast was gone by the 3.5 s wait. The frame
was taken after the first HUD gate screenshot, so it is roughly 1 to 2 s after the advance (not timed precisely; the
brief asked for about 1 s).

**2. thirsty crop and soil:** measured against the fresh-control tiles in the same frame, the tint is the multiply the
engine uses (`0xE6C070`, expected ratio about 0.90, 0.75, 0.44):

| Viewport | Thirsty bare soil B / fresh soil K (R, G, B) | Thirsty sprout C / healthy H (leaves, R, G, B) |
|:--|:--|:--|
| 1280x720 | 0.92, 0.77, 0.46 | 0.98, 0.80, 0.46 |
| 1920x1080 | 0.92, 0.77, 0.46 | 0.98, 0.80, 0.46 |
| 390x844 | 0.92, 0.77, 0.46 | 0.98, 0.80, 0.46 |

Visibly: B reads as darker, warmer brown than the fresh tiles; C has yellow-olive leaves against the bright green of H.
Thirsty seed E (stage 0, one dry day) does not read differently from fresh seed G at native size; the seed sprite is a few
dots on the soil texture. So the "thirsty planted crop" shown clearly is C, a grown crop.

**3. withered crop:** A is drawn dark olive-brown on the dark crop backdrop. It is withered (the message does not count it,
and the toast does not mention it). It is not a clearly brown stalk: its leaf pixels average about (35, 43, 14), dark and
low contrast. See defects.

**4. watered control:** H is bright green, with no tint (leaves about (82, 118, 60)), and K and G are fresh controls
(no tint). The control confirms that watering on D+2 is enough to avoid the warning.

Other checks: the shared dev clock advances only the page that calls it (see defects). Each viewport's own page advanced
its own run, so each frame is consistent with its own calendar.

## UI standard check (docs/design/ui-standard.md, section 7)

Only English was captured. Vietnamese fit was not checked. Before frames were not captured (the change adds a line and
does not move any existing element).

| # | Item | Result | Note |
|:--|:--|:--|:--|
| 1 | Element sits in its region (section 2) | Pass | The toast is in the bottom-centre toast line above the hotbar. |
| 2 | Nothing overlaps | Pass | At 1280 and 1920 the toast clears the hotbar. At 390 it clears the Use button, with a few pixels of gap above the hotbar (by eye). |
| 3 | Menu column: one column, at most 8 buttons | Not checked | Not changed by this commit. |
| 4 | Edges line up | Not checked | No card changed. |
| 5 | Panels use GamePanel | Not applicable | No panel. |
| 6 | Three font sizes, no wrap, nothing clipped | Pass (English) | One line, not clipped. Wording issue: see defects. |
| 7 | No inner scrollbar at 1280x720 and up | Pass | None visible. |
| 8 | Item icons whole and centred | Pass | Hotbar icons whole; unchanged. |
| 9 | Spacing in `--u` multiples | Not checked | Needs CSS or DOM measurement, not visible in screenshots. |
| 10 | Vietnamese and English both fit | Not verified | Vietnamese string is `{0} cây cần tưới nước`; not captured. |

## Defects and notes

- **Singular form (English, defect).** With one thirsty crop the message reads "1 crops need water" (see
  `defect-singular-toast-1280x720.png`). `Farm_Thirsty_Morning` in `Language.en.resx` has no plural form. The
  Vietnamese string has no such problem.
- **Withered crop is not clearly brown.** The withered sprite is drawn dark olive on the dark planting backdrop, so
  it reads as nearly black. The brown tint (`0x8B6B4A`) may be too dark against the backdrop. Not confirmed as a bug.
- **Thirsty stage-0 seed is not distinguishable.** E and the fresh seed G look the same at this size. The tint is applied
  (see the sprout check on C), but a seed does not show it clearly.
- **Clock skip is per page (dev tool).** `nosumQa.advanceClock` updates only the calling page's calendar. In an early
  run the 1920 and 390 pages kept the old day while the 1280 page moved on. Each run now advances its own page. This is a
  development-tool behaviour, not a gameplay one.
- **Season skip sets midnight (dev tool).** `nosumQa.season` lands on the first game day of the next season, at 12 AM,
  which renders dark. The server was restarted to return the clock to real time; the final frames are at 10 AM.
- **New-season banner.** In an earlier 1920 run that crossed into Summer the top-centre banner "NEW SEASON" showed at the
  rollover, in its own region and clear of the toast line. It did not appear in the final runs.

## Follow-up

- The English singular defect ("1 crops need water") is fixed: one at-risk crop now reads "1 crop needs water" (`Farm_Thirsty_Morning_One`). `defect-singular-toast-1280x720.png` is the capture that showed it.
- Open for a human look: the withered sprite reads dark olive on the dark backdrop rather than a clear brown stalk, and a thirsty seed (stage 0) looks like a fresh seed at native size. Not changed in this commit.
