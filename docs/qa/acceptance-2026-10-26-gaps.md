# Browser acceptance: gaps of the 2026-10-26 final run

This run closes part of the gaps listed in [`acceptance-2026-10-26-final.md`](acceptance-2026-10-26-final.md). It was run on `master` at
`a8f9b01` (the host and the client built from this branch). It is **partial**: the items marked "not run" below were not executed, so they
are open gaps, not passes.

## How it was run

- Host: `DOTNET_ENVIRONMENT=Development dotnet run --no-build --project server/Nosum.Host` on PostgreSQL 16 (role `nosum`, database `nosum_dev`).
- Client: `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`, served as static files on port 8080.
- Chromium through Playwright (`NODE_PATH=$(npm root -g)`). Each configuration is one run of the script: 1280x720 and 1920x1080, Vietnamese
  (`vi-VN`) and English (`en-US`, set in-game through Settings, as a player does it).
- Console errors, page errors and failed requests were collected on every page of every run. Scripts and screenshots are scratch files and
  are not committed.
- Existing checks rerun on this build (`tests/browser/`):
  - `menu-fit-check.js`: 11 tabs, 4 sizes, 2 languages. PASS, no console errors.
  - `chat-check.js`: PASS, no console errors.
  - `audio-check.js` (with a `.png` path): PASS. `audio-module-check.js`: PASS. `music-check.js spring_day`: PASS.

## Results

Pass/fail per item, viewport and language. "n/a" means the item does not depend on the viewport or language.

### Checklist item 1: start and account

Script for each configuration: password recovery (a Gmail address with a hyphen is refused, a dotted address moves on to the code step),
register, rename (diacritics refused, profanity refused, valid name accepted, a second change within 24 h refused), guest rename note.
Messages were read from the page text, not assumed.

| Check | 1280x720 vi | 1280x720 en | 1920x1080 vi | 1920x1080 en |
|:--|:--|:--|:--|:--|
| Recovery refuses a non-Gmail-valid address | Pass | Pass | Pass | Pass |
| Recovery sends the request (code step shown) | Pass | Pass | Pass | Pass |
| Register reaches the game | Pass | Pass | Pass | Pass |
| Rename refuses diacritics | Pass | Pass | Pass | Pass |
| Rename refuses profanity | Pass | Pass | Pass | Pass |
| Rename accepts a valid name | Pass | Pass | Pass | Pass |
| Second rename within 24 h refused | Pass | Pass | Pass | Pass |
| Guest cannot rename (note, no name field) | Pass | Pass | Pass | Pass |
| Console errors | none | none | none | none |

Not run: login with a wrong password and the correct one were covered by the 10-26 final run and were not repeated here.

### Checklist item 2: UI

Script for each configuration: first-run hint, walking and using a tool, hotbar, Settings pages, and (separately, in a Pixel 5 profile
of 393x727 with touch) the touch stick and use button.

| Check | 1280x720 vi | 1280x720 en | 1920x1080 vi | 1920x1080 en |
|:--|:--|:--|:--|:--|
| First-run hint shown | Pass | Pass | Pass | Pass |
| Hint advances after walking and using a tool | Pass | Pass | Pass | Pass |
| Hotbar has nine slots | Pass | Pass | Pass | Pass |
| Hotbar: key 3 selects slot 3 (asserted) | Pass | Pass | Pass | Pass |
| Hotbar: click on slot 6 selects it (asserted) | Pass | Pass | Pass | Pass |
| Hotbar: key 1 returns to slot 1 (asserted) | Pass | Pass | Pass | Pass |
| Settings: Display page opens | Pass | Pass | Pass | Pass |
| Settings: Controls page opens | Pass | Pass | Pass | Pass |
| Settings: Sound page opens | Pass | Pass | Pass | Pass |
| Settings: General page shows account, sign out, language | Pass (see note) | Pass (see note) | Pass (see note) | Pass (see note) |
| Console errors (desktop) | none | none | none | none |
| Touch: stick and use button present, inside the viewport (393x727) | Pass | Pass | Pass | Pass |
| Touch: dragging the stick moves the knob (40 px) | Pass | Pass | Pass | Pass |
| Touch: use button runs, no errors | Pass | Pass | Pass | Pass |

Notes:
- The General check compared the page text with the baseline taken while General was already open, so the script reported a fail; the page
  content (account, sign out, language) is in the run's output and passes when read.
- The touch drag was measured on the stick's knob. It did not measure the farmer's position, so "moves the farmer" is not confirmed.
- Touch controls were run at the Pixel 5 portrait size only. The landscape profile (802x293) of the 10-25 run was not repeated.

### Checklist items 3 to 9 and item 4 (art)

| Item | Result |
|:--|:--|
| 3. Farming: till, plant | Pass at 1280x720 vi (tilled plot shown, planting objective 1/1, one run) |
| 3. Farming: water | Not confirmed (the watering can was used; no visual or state check of the watered plot) |
| 3. Farming: crop growth over days and harvest | **Not confirmed**: after a 3-day skip the harvest objective still reads 0/1 |
| 3. Farming: seasons, rain, day and night tint, sprinklers | **Not run** |
| 4. World (chop, break, cut, forage, ground items, chunk streaming, warps, beach bridge) | **Not run** |
| 5. Economy (counters, market, shipping, trade, mail) | **Not run** |
| 6. Progress (energy, eating, skills, crafting, furnace, tool upgrade, mine, elevator) | **Not run** |
| 7. Animals (coop, chicken, feed, egg) | **Not run** |
| 8. Social and co-op (chat, friends, visits, co-op, warehouse, upgrade) | **Not run** (`chat-check.js` passed; the rest needs two players) |
| 9. Quests (tutorial chain, requests board) | **Not run** |
| M6 art: crop growth stages, animals, coop and barn buildings and interiors, farmer hair, skin and wardrobe | **Not run** |

## Coop and barn: "map visits read back as the farm"

This is intended behaviour, not a bug. Code and records:

- `server/Nosum.Infrastructure/Players/PlayerService.cs`, `StartingPlaceAsync` (around line 226): a saved place whose map is an interior is
  skipped, and the player starts on the farm. The comment there says: "The inside of a farm building belongs to the building, not to the
  player: a player who left the game in there starts on the farm."
- `docs/adr/ADR-028` (decision D2), in the same sentence as the door rule: "A player who left the game inside restarts on the farm, because the
  saved place of an interior is not the player's."

The earlier observation came from a test account whose `player_states` row was set to a coop or barn map directly and then logged in. The
server does what ADR-028 says. No code change was made. What was **not** run: entering an owned, finished coop or barn through its door in a
browser. That path (`PlayerService.cs`, the door warp around line 198) is open to browser confirmation.

## Open items

- Items 3 (harvest and growth), 3 (water and the rest of farming), 4 to 9 and the M6 art checks are not run or not confirmed (see table).
- Coop or barn entry through a door in a browser: not run.
- Touch controls at the 802x293 landscape size: not run in this session.
- Item 1 login with a correct and a wrong password: not repeated here (covered by the 10-26 final run).
