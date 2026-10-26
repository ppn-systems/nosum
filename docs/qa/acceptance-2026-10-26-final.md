# Browser acceptance run, 2026-10-26 (final, on master)

The roadmap's definition of done ([`docs/design/roadmap.md`](../design/roadmap.md)) asks for a browser run of every feature at 1280x720 and
1920x1080, in Vietnamese and in English, with no console errors. This run is on `master` at `e18a1d1`, after M6 (own art, own music, the phone
menu fix, `Server:Url`). It repeats the checks of [`acceptance-2026-10-25.md`](acceptance-2026-10-25.md) where time allowed and adds the M6 checks.

**Coverage is partial.** Checklist items 3 to 9 (farming, world, economy, progress, animals, social, quests) were **not re-run in the browser**
in this session. That is a gap in the acceptance, not a pass: the previous run covered them on M1 to M5, and M6 did not change their logic
(the server and client tests of those features pass, see the gates). The M6 checks, the phone menu, the UI smoke pass and the start/account
checks listed below were run and are recorded here.

## Result

| Area | Result |
|:--|:--|
| Gates: 8 `.slnx` build, 0 warnings, 0 errors | Pass |
| Gates: `dotnet test tests/Nosum.Tests.slnx` run twice | Pass (both runs: 2300 unit, 672 integration, 0 failed, 0 skipped) |
| Release gate (removed in ADR-042 P7) | Pass (exit 0) |
| 1. Start and account (register, login, wrong password refused) | Pass for the items run; rename, guest rename and password-recovery form **not re-run** |
| 2. UI, game menu at four sizes in both languages | Pass (`tests/browser/menu-fit-check.js`: all 11 tabs inside and reachable, 8 combinations) |
| 2. UI, settings, inventory, hotbar, language switch | Pass at 1280x720 and 1920x1080 (smoke pass: inventory, hotbar key, Vi to En and back, Esc). Hotbar selection was sent but not asserted; touch controls and first-run hints **not re-run** |
| 3 to 9. Farming, world, economy, progress, animals, social, quests | **Not re-run in the browser** (see above) |
| 10. Audio (no `AudioContext` before the click, sounds play) | Pass (`audio-check.js`, `audio-module-check.js`) |
| M6 art: maps, tilesheets, network 404s | Pass for the maps visited; 404 check Pass (see below) |
| M6 art: farmer, items, crops, animals, buildings | Farmer, items and UI checked by eye; crops, animals and the coop/barn buildings **not checked** |
| M6 music: starts after first click, day and night track | Pass (`music-check.js`: `spring_day` at 09:20, `spring_night` with the night epoch) |
| M6 music: volume and mute | Pass (recorded bus gains: music 0 gives 0, sound 30 gives 0.09, mute gives 0 on both buses, unmute restores 0.49) |
| M6 music: sound effects | Pass (`audio-check.js`: footsteps, tool swing, panel open and close) |
| M6 phone menu at 802x293 and 393x727 | Pass (all 11 tabs reachable in Vi and En, no console errors). F1 and F2 of the previous run are **closed** |
| M6 client server address, unset | Pass (falls back to the page's host; sign-in and play work) |
| M6 client server address, `Server:Url` set | Pass (`ws://localhost:57300/ws/` connects, no WebSocket errors; a dead port is refused on that URL) |
| Chat (F3 of the previous run) | Pass (`chat-check.js`, no page error on close) |

Open failures in this run: **none** among the items executed. The unexecuted items above are open coverage gaps.

## Gates

- All 8 `.slnx` (`git ls-files '*.slnx'`) built with `--no-incremental`: 0 warnings, 0 errors.
- `dotnet test tests/Nosum.Tests.slnx`: run 1 and run 2 each passed 2300 unit tests and 672 integration tests, none failed, none skipped. The
  counts are higher than the previous run's 2111 and 588 because of tests added since M5.
- The release gate (removed in ADR-042 P7) exited 0.
- Static check of the art: all 97 tilesheet references in `art/own/Maps/*.tmx` (and `Mines/`) resolve to files in the published bundle.

## How it was run

- Host: `DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host` on PostgreSQL 16 (`nosum_dev`). Restarted before each map visit
  and once with `NOSUM_CALENDAR_EPOCH` set so that the server clock read night (the epoch is ISO 8601 UTC, see `Program.cs`).
- Client: `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`, served as static files (port 8080). A second and third copy, with
  `wwwroot/appsettings.json` edited to set `Server:Url`, were served on ports 8081 and 8082 for the server-address checks. The publish output was
  edited, not the repository.
- Chromium through Playwright (`NODE_PATH=$(npm root -g)`). Scripts and screenshots live in the session's scratch area and are not committed.
- Accounts: one registered test account (`artqa1`), created through the UI, moved between maps by setting its `player_states` row while the host
  was restarted, then logged in again. Guests for the smoke and menu passes.

## M6 checks in detail

**Art.** Each map was visited through its saved place, and each visit was checked for 404s, failed requests and console errors. Result: none
for `farm`, `farmhouse`, `busstop`, `forest`, `town`, `backwoods`, `mountain`, `beach`, `woods`, `mine` at 1280x720. The screenshots of those
maps were reviewed as a contact sheet at reduced size: the tiles, trees, water, paths and cave walls render from the own art with no missing
or garbled tiles, and the farmhouse interior is drawn with its black map edge. `coop` and `barn` read back as the farm: the server placed the test
account on its farm instead (observed; the account owns no building there, and the cause was not traced further). `coopfarm` was not visited (it needs a co-op membership).
The farmer was walked up, down, left and right from the farm; the frames show the farmer walking into the farmhouse, but the four facings were
judged at small size, not at full resolution. Hair, skin and wardrobe options were **not** checked. The hotbar and the inventory show their own
icons.

**Music.** Before the first click no music loop started; after the click one loop started and kept playing with no second loop. Day
(`spring_day`, 09:20) and night (`spring_night`, with `NOSUM_CALENDAR_EPOCH` set for a night clock) were both recognised by length.
The day to night change was tested by restarting the host with a different epoch, not with the dev clock's day skip.

**Volume and mute.** Set on Settings, Sound: the music slider drives the music bus (0 to 0, 70 to 0.49) and the sound slider drives the sound
bus (30 to 0.09). The mute box sets both buses to 0 and unmuting restores 0.49 on both. No console errors.

**Phone menu.** At 802x293 and 393x727 every tab of the menu (11 in English, 11 in Vietnamese) is inside the viewport and reachable by
scrolling its page; the window fits. The game menu's tabs were not clipped.

**Client server address.** Three configurations: `Server:Url` absent (the page is `localhost:8080`, the client uses the page's host and
port 57300), works. `Server:Url` set to `ws://localhost:57300/ws/` on a page served from `127.0.0.1:8081`: works, with no WebSocket errors.
`Server:Url` set to `ws://localhost:57399/ws/` (nothing listens there): the WebSocket to that URL is refused with `ERR_CONNECTION_REFUSED`, so
the configured value is what the client uses. The farm then runs offline, as the wasm README describes.

## Checklist, items 1 to 10

| Item | Result |
|:--|:--|
| 1. Guest start | Not re-run (the guest path was used by the smoke pass and the menu pass, which both reached the game) |
| 1. Register (Gmail, 8+ characters, matching confirmation) | Pass (1280x720 Vi) |
| 1. Login, wrong password refused | Pass (refused with the error message, 1280x720 Vi); correct password Pass |
| 1. Rename rules, guest cannot rename, password recovery form | **Not re-run** |
| 2. Game menu, every tab fits, both desktop sizes, both languages | Pass |
| 2. Settings gear and its pages | Pass for the General page (1920x1080, English screenshot) and the Sound page (1280x720, volume and mute); Display and Controls not opened |
| 2. Inventory (Esc closes), hotbar | Pass for inventory and Esc; hotbar key sent, selection not asserted |
| 2. First-run hints, language switch | Language switch Pass; first-run hints **not re-run** |
| 2. Touch controls (Pixel 5 profile) | **Not re-run** |
| 10. Audio | Pass |
| 3 to 9 | **Not re-run in the browser** |

Observation, not scored: the start screen's background is a live canvas, so a canvas on the page is not a sign that a game has started;
the scripts check the HUD or the account's place instead.

Observation, not scored: `tests/browser/audio-check.js` writes `audio-game.png` into the working directory when no screenshot path is given.
Run it with a path (`node tests/browser/audio-check.js <url> <path>`) so no stray file lands in the repository.

## Harness notes (not product failures)

- A saved place edited in PostgreSQL is not read back while the host runs: the host keeps the player's state in memory after the last leave.
  The host was restarted before each map visit to get a clean read. Production has no such step.
- The host warns at start-up that `NOSUM_SMTP_HOST` is not set, so password recovery codes are not delivered. This is expected in development.
- The server writes a player's place when the player leaves, so each visit's place was read back from `player_states` after logout.
