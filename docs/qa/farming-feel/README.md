# QA captures: farming feel (ready sparkle, farming juice, hold-to-repeat)

Branch `claude/farming-feel`, HEAD `618efed` (merge of origin/master). Second run: the ready sparkle, the unripe control, the
till juice and the 390x844 captures. No source file changed; no code bug was found in this run.

## Status (second run)

| Item | Result |
|:--|:--|
| Ripe sparkle (day 6), 1280x720 vi and en | **Captured.** Gold sparkle at the tile's top-right corner. |
| Unripe control (day 5 on the same tile), 1280x720 vi and en | **Captured.** No sparkle. |
| Ripe sparkle and control, 390x844 vi and en | **Captured** from the width-override copy (see below). |
| Till juice first frame, 390x844 vi and en | **Captured** (flecks at the target). |
| Harvest pop (scythe on the ripe radish) | **Not captured.** No harvest happened in any attempt (see Harvest). |
| Coin pop (sale at the shipping bin) | **Not captured.** No radish was harvested, so nothing was sold. |
| Reduced motion | **Inconclusive.** Both modes changed the tile between the two frames (see below). |

## Why the sparkle was never drawn in the earlier attempt

Found in this run: **the planting was refused by the season, not by a code bug.** The planting refusal toast reads
"Cây này không trồng được vào mùa này." (This crop cannot be planted in this season). The HUD showed Đông (winter).
Each capture run advances the dev clock by 7 to 9 game days (`advanceClock`), and the clock is in memory, so the earlier
runs had moved the season on until radish could no longer be planted. The seed count then stays at 6 and the crop never
exists, so it never ripens and no sparkle is drawn. The seed count in the earlier note (stayed at 6) matches this refusal.

Restarting the server (same environment) resets the dev clock to the epoch: the HUD read Xuân (spring) day 13 after a
restart. Each capture below was made after a restart or early in a season.

The ripening rule was confirmed: the radish was watered on days 0 to 5 (six watered days). The sparkle first showed on day 6.

## Steps used (second run)

1. Published `618efed` with `-p:NosumDevTools=true` (Release) to `scratchpad/qa2/pub`. `ready.png` and `drop.png` present, and
   `ready.png` returned HTTP 200 from the static server.
2. Servers: `python3 -m http.server 5820` on `pub/wwwroot`; game server `DOTNET_ENVIRONMENT=Development`
   `NOSUM_CALENDAR_EPOCH=2026-10-26T05:16:21Z`, WebSocket 57300. Restarted before the runs that needed spring.
3. Ripening (per capture run): till tile 62 with the hoe (slot 1), plant radish seed (slot 4), water with the can (slot 2) on
   day 0; `advanceClock(1)` then water on days 1 to 5; control frame on day 5; `advanceClock(1)` to day 6 for the sparkle frame.
   Each capture first teleports the farmer onto its own tile (62, 27): the sparkle is drawn from the client's copy of the tile,
   and the copy refreshed after a teleport.
4. Planting was confirmed by the seed count (6 to 5 after one planting in the smoke test) and by the sprout on the tile.
5. Captures are tile crops (80 x 120 px around tile 62, the farmer above it) and full frames. The `-x4` files are
   nearest-neighbour x4 of the tile crops. Full frames have no x4 file (a 4x full frame would be 5120 x 2880).
6. **390x844 copy:** `scratchpad/qa2/pub390/wwwroot/appsettings.json` was edited there only, `Device.MinimumWidth` 1024 to 320,
   and served on port 5821 with a second static server. The repo's `wasm/Nosum.Wasm/wwwroot/appsettings.json` and the published
   copy at `pub/` are unchanged (still 1024). **The 390x844 frames were taken with the width override.** The game then
   renders; the layout was not checked against the product gate at 390 px.
7. Reduced motion: `reducedMotion: 'reduce'` and `'no-preference'` in English, 1280x720, till press on tile 62, two tile
   crops 100 ms apart (`scratchpad/qa2/reduced.cjs`).

## Harvest (not captured)

- The reticle is white before a scythe press on the ripe tile (allowed), and the press shows colourful flecks at the target.
- The radish stays on the tile after the press (frames 1.2 s later), no radish appears in the hotbar, and no gold harvest
  pop shows. Three presses at 1.5 s apart also left it in place.
- The server's harvest verdict is not visible from the client. The database check for this session was inconclusive: the
  rows written in this session were not found in the window I queried, so the server outcome is not confirmed either way.
- A separate observation: in one run the radish was gone on day 7 after ripening on day 6 without any press. In another run
  the tile was bare soil at day 6 after a day-6 watering. Cause not established. `docs/adr/ADR-021` says an unharvested ripe
  crop follows the same dry-day wither rule and the day sweep clears withered crops, which may explain it; this is not verified.

## Coin pop (not captured)

No radish was harvested in this run, so the bin sale was not attempted with a radish. The bin is at farm tile (71, 14)
(`FarmProps.ShippingBin`, a 2 x 1 footprint). No coin pop was observed.

## Reduced motion (inconclusive)

The two frames 100 ms apart differ in the tile area in both modes (about 3000 changed pixels in both, lower-tile bounding box
identical). The till itself changes the tile between the frames, so the flecks cannot be separated from the tile change. **No
reduced-motion result is claimed.**

## Files (second run)

All files are 1280x720 or 390x844 full frames, plus tile crops (80 x 120) and their x4 versions. `<lang>` is `vi` or `en`.

| File | Shows |
|:--|:--|
| `sparkle-ripe-<lang>-1280x720.png`, `sparkle-ripe-tile-<lang>-1280x720.png` (+ `-x4`) | Day 6, radish ripe, gold sparkle at the tile's top-right corner. |
| `sparkle-control-<lang>-1280x720.png`, `sparkle-control-tile-<lang>-1280x720.png` (+ `-x4`) | Day 5, same tile, unripe: no sparkle. |
| `sparkle-ripe-<lang>-390x844.png`, `sparkle-ripe-tile-<lang>-390x844.png` (+ `-x4`) | As above at 390x844, with the width override (see step 6). |
| `sparkle-control-<lang>-390x844.png`, `sparkle-control-tile-<lang>-390x844.png` (+ `-x4`) | Control at 390x844, with the override. |
| `juice-till-<lang>-390x844.png`, `juice-till-tile-<lang>-390x844.png` (+ `-x4`) | First frame after the hoe press at 390x844: flecks at the target. The en 1280 run did not take a juice frame. |
| `juice-till-vi-1280x720.png` (first run, unchanged) | Till juice at 1280x720 vi from the first run. Not re-captured here. |
| `harvest-attempt-vi-1280x720.png`, `harvest-attempt-{before,after}-tile-vi-1280x720.png` (+ `-x4`) | Scythe press on the ripe radish, day 6: the crop is still there after the press (negative). |
| `reduced-motion-{reduce,no-preference}-{a,b}-tile.png` (+ `-x4`) | Tile frames 100 ms apart after a till press, English. Inconclusive. |

Files from the first run that this run supersedes: `sparkle-attempt-*` (the radish there was not ripe, so it did not test the
sparkle). They are kept as the record of the earlier miss. The sparkle files above replace them as the evidence.

## Earlier run (superseded where stated)

Superseded by the second run: the "Blocked and not captured" section below. The ready-sparkle cause is now found (planting refused
by the season, see above), and the 390x844 captures exist (with the width override). The rest of the earlier text stands.


Branch `claude/farming-feel`, HEAD `86430ee`. Commits under test (from `git log --oneline -4`):

- `86430ee` feat(game): farming juice for till, plant, water, harvest and the bin payment (backlog 9)
- `86167ca` feat(game): hold the use button to repeat the tool at its swing rate (backlog 5)
- `fe75787` feat(wasm): ready sparkle over ripe crops (backlog 2)
- `fd9cba9` docs(design): research on HUD, gameplay loops and co-op UX with a 12-item backlog (#270)

No source file was changed. Status: **partly captured.** The ready sparkle was not captured (blocker below), the 390x844
captures were blocked by the device gate, the reduced-motion check is inconclusive, and the coin pop and harvest pop were
not captured.

## Steps used

1. PostgreSQL 16 started (`service postgresql start`, `pg_isready` ok). The role `nosum` and database `nosum_dev` did not
   exist and were created with the development connection string from `NosumDatabase.DevelopmentConnectionString`.
2. Client published: `dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumDevTools=true --no-restore`
   into the session scratchpad (`ready.png`, `drop.png` present in `assets/sprites/`).
3. Static client: `python3 -m http.server 5820 --bind 127.0.0.1` on the published `wwwroot`.
4. Server: `DOTNET_ENVIRONMENT=Development NOSUM_CALENDAR_EPOCH=2026-10-26T05:16:21Z dotnet run --project server/Nosum.Host --no-restore`,
   WebSocket 57300. The epoch was set so the in-game clock reads about 11:00 (the default epoch read 21:30, night).
5. Language: `localStorage['nosum.settings.v1']` = `lang=1` (vi) or `lang=2` (en). Hooks: `?qa=1`, `window.nosumQa.teleport`,
   `advanceClock`. Each run used a fresh guest.
6. Capture driver (scratchpad only, not in the repo): Playwright from `require('playwright')`, default Chromium.
   Teleports are rate limited by the server (8 per 4 s) and need about 1.5 s to land; the farmer is kept centred by the
   camera, so the tile below the farmer is always the target.

## Files

| File | Shows |
|:--|:--|
| `juice-till-<lang>-1280x720.png`, `-strip.png`, `-strip-x4.png` | Till press on tile 65 (hoe). First frame after the press: brown soil flecks at the target. |
| `juice-plant-<lang>-1280x720.png`, `-strip.png`, `-strip-x4.png` | Plant press on tile 63 (radish seed). First frame: green sprout flecks. |
| `juice-water-<lang>-1280x720.png`, `-strip.png`, `-strip-x4.png` | Water press on tile 64 (can). First frame: blue drops at the target. |
| `sparkle-attempt-<lang>-1280x720.png`, `-strip.png`, `-strip-x4.png` | The ripe-day capture that failed: radish on tile 62 is not ripe, no sparkle. Tile 61 (unripe control) and tile 64 show sprouts and no sparkle. |

`<lang>` is `vi` or `en`. Both languages have the till, plant and water files at 1280x720. Harvest and sale files were removed
because they were not verified (see below). No 390x844 files exist.

## Measured timings (1280x720)

Method: `t0` = `performance.now()` read just before the mouse-down that presses the action. Each action then took
full-page PNG screenshots one after another; each screenshot's start and end are measured against `t0`. "Flecks visible"
means the first screenshot in which the tile region differs from the final frame, which is after the flecks have expired.
That region also contains the farmer's tool swing, so these are the first frames with change, not pure fleck counts.

| Action | Language | First screenshot start after press | End | Frame |
|:--|:--|:--|:--|:--|
| Till | vi | 80 ms | 148 ms | frame 0 |
| Plant | vi | 52 ms | 119 ms | frame 0 |
| Water | vi | 62 ms | 138 ms | frame 0 |
| Till | en | 53 ms | 139 ms | frame 0 |
| Plant | en | 70 ms | 168 ms | frame 0 |
| Water | en | 52 ms | 136 ms | frame 0 |

Each first frame starts within 200 ms of the press (the screenshot itself takes 40 to 90 ms, so the image is at most about
140 ms old). The flecks are gone after roughly 450 ms in the later frames. Only the first frames are saved.

## Hold-to-repeat (functional, 1280x720 vi)

Hoe in slot 1, facing tile 62 (grass, then tilled). One E tap, then E held for 2.006 s. Packets counted from the page's
WebSocket frames (the 55-byte request that the single tap sends, with its sequence byte; each request got an 8-byte reply).

- Requests during the hold: **4**, at 223, 746, 1233 and 1736 ms after key down.
- Gaps between requests: 523, 487 and 503 ms (expected about 500 ms: 420 ms swing plus 80 ms recovery).
- Expected for 2 s: about 4. Matches.
- I did not decode the opcode; "farm use" is inferred from the single tap sending exactly this packet.
- I did not check each reply's outcome, so "the server cooldown is respected" is not verified beyond the request cadence.

## Reduced motion (not conclusive)

Two runs at 1280x720 vi (`reducedMotion: 'reduce'` and `'no-preference'`), watering can on tile 62, eight clip frames
(about 100 ms apart) of the tile area. I tried to measure the water flecks by colour, but a static blue object in the same
place in both runs swamped the count, so the positions could not be compared. **No reduced-motion result is claimed.**
The frames are in the run folder, not in the repo.

## Blocked and not captured

- **Ready sparkle: not captured.** A radish planted on tile 62 was not drawn with a sparkle on any frame I took. The
  sparkle draws only when the client's `IsMature` holds. A radish needs six watered days. In several runs the tile
  looked empty after the planting or after the sixth day, and the seed count stayed at 6, so the planting itself was
  not reliably accepted. I could not find the cause in the time available. The browser does request `ready.png` (HTTP 200).
- **Harvest pop: not captured.** No ripe radish was available.
- **Coin pop (sale): not captured.** No harvested radish to sell.
- **390x844 captures: blocked.** `wasm/Nosum.Wasm/wwwroot/appsettings.json` sets `Device.MinimumWidth` to 1024. A 390-wide
  window shows the "too narrow" overlay (`DeviceGuard.razor`), and the sign-in button was covered. No farm frame at 390x844
  was produced in this run.

## Console and page errors

Checked on every run: zero `pageerror` and zero `console.error` events in the captured runs (`errors: []`). The server
log has two expected notices (Google and Microsoft sign-in client IDs not set). Server handlers logged `handler.slow`
warnings (100 to 470 ms) during the farm actions.

## Defects and notes

- **Target highlight covers the farm tile.** The target corners sit on the tile the farmer faces. This hides the tile
  edge that the sparkle or drop would use (cosmetic, same as the earlier decay captures).
- **A ripe radish disappears the day after it ripens in the probes.** Between days 5 and 6 of the probe the crop
  vanished from the tile with no visible wither state, and a scythe press on day 5 removed it without a radish in the
  hotbar. Whether this is expected harvest-on-press or a bug was not established.
- **Device gate at 390 wide.** A phone-sized window cannot reach the game (`MinimumWidth` 1024), so the 390x844 requirement
  of the QA plan cannot be met as written.
