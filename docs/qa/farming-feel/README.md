# QA captures: farming feel (ready sparkle, farming juice, hold-to-repeat)

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
