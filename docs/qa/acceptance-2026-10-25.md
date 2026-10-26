# Browser acceptance run, 2026-10-25

The roadmap's definition of done ([`docs/design/roadmap.md`](../design/roadmap.md)) asks for a browser run of every milestone feature at
1280x720 and 1920x1080, in Vietnamese and in English, with no console errors. This is that run for M1-M5 on `master` at `74eb211`.

## Result

| | |
|:--|:--|
| Items 1, 3, 4, 5, 6, 7, 9, 10 | Pass |
| Item 8 (social and co-op) | Fail (fixed): the chat raised a page error (F3); everything else passed |
| Item 2 (UI) | Fail (open): the game menu does not fit phone-sized viewports (F1, F2); everything else passed, including touch controls |
| Fixed | F3 (chat page error), F4 (missing tester export) |
| Open | F1, F2 |

Open failures:

- **F1** the game menu is taller than a landscape phone viewport (Inventory and Skills pages are cut off).
- **F2** the menu's tab strip is wider than a portrait phone viewport (the Settings tab is clipped).

## Gates

- All 8 `.slnx` (`git ls-files '*.slnx'`) build with `--no-incremental`: 0 warnings, 0 errors.
- `dotnet test tests/Nosum.Tests.slnx` passed twice (2111 unit tests and 588 integration tests each time, none skipped).

## How it was run

- Host: `DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host` on PostgreSQL 16 (`nosum_dev`). The host was restarted before
  every farming pass so the tester clock offset (`DevTimeProvider`) starts from the calendar's own day.
- Client: `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`, served as static files, Chromium through Playwright.
- Each checklist item is one Node script run once per configuration (1280x720 vi, 1280x720 en, 1920x1080 vi, 1920x1080 en). English means
  the account language set through Settings, as a player does it. Two browser contexts are the two players of the multiplayer checks.
- Outcomes are read from the UI and from the database (`players`, `farm_plots`, `inventory_slots`, `coin_log`, `farm_buildings`, ...), not from
  assumptions. Console errors, page errors and failed requests are collected on every page.
- Scripts and screenshots live in the session's scratch area and are not committed. The one reusable check that came out of the run is
  `tests/browser/chat-check.js`.
- Tester shortcuts: the dev grant and clock tools (`DevTools.Grant`, `DevTools.AdvanceClock`), and moving a registered account's saved place
  (`player_states`) while it is signed out to stand in front of a counter or an exit. Walking is dead reckoning (5 tiles per second), so each
  warp and each exit was still crossed on foot.
- Limits of the harness, not findings: a signed-in client keeps the clock it estimated at sign-in, so after a tester clock skip the calendar and the
  furnace countdown show the old time until the next sign-in; scripts sign in again after a skip. Walking by timed key holds drifts by a tile now and
  then, so a few steps were repeated until they landed (the farming sprinkler step is the one where two English runs did not land and the Vietnamese
  runs at both sizes are the evidence).

## Checklist

### 1. Start and account

| Check | Result |
|:--|:--|
| Guest start | Pass |
| Register (Gmail only, 8+ character password, matching confirmation, nickname rules) | Pass |
| Login, wrong password refused | Pass |
| Rename: diacritics refused, profanity refused, valid rename accepted, second rename within 24 h refused | Pass |
| Guests cannot rename (profile shows the note, no input) | Pass |
| Password recovery form opens and sends the request | Pass |

Observation, not scored: the start screen is Vietnamese whatever the browser language; English applies once the account's language is set.

### 2. UI

| Check | Result |
|:--|:--|
| Game menu: every tab opens and fits at both desktop sizes in both languages; no Market tab (ADR-034) | Pass |
| Settings gear and its four pages (General, Display, Sound, Controls) | Pass |
| Minecraft-style inventory (36 cells, Esc closes) | Pass |
| Hotbar (nine slots, number keys and clicks select) | Pass |
| First-run hints (first hint, advance after walking and using a tool, switch off in Settings) | Pass |
| Language switch (menu text changes, switching back restores it) | Pass |
| Touch controls on a Pixel 5 profile (touch emulation): stick and use button present and inside the viewport, dragging the stick moves the farmer, the use button works a tool | Pass |
| Game menu on phone-sized viewports (802x293 landscape, 393x727 portrait) | **Fail (open)**, F1 and F2 below |

### 3. Farming

| Check | Result |
|:--|:--|
| Till, plant, water | Pass |
| Crop growth over advanced days and harvest | Pass |
| Harvest drops are picked up into the inventory | Pass |
| Seasons (calendar changes season after advancing days) | Pass |
| Rain (day 14 of spring rains; an unwatered crop planted that day counts the rain as a watered day; rain is drawn) | Pass |
| Day and night tint (mean luminance of the farm view 138 at 18:30, 53 at 21:30) | Pass |
| Sprinklers (a crop beside a sprinkler matured with no hand watering while a control crop three tiles away did not) | Pass |

### 4. World

| Check | Result |
|:--|:--|
| Chop trees (axe), break stones (pickaxe), cut weeds (scythe), yields reach the inventory | Pass |
| Forage on the Beach (shore items appear after a game day and can be picked up) | Pass |
| Ground items (picked up on walking near; no stale rows left) | Pass |
| Chunk streaming while walking far (47 tiles down the Forest, three chunk borders, no errors) | Pass |
| Warps: Farm to Bus Stop, Forest and Backwoods; Bus Stop to Town; Town to Mountain, Forest and Beach; Forest to Woods; Mountain to Mine | Pass |
| Beach bridge locked (the farmer stops at the west bank) | Pass |

Two players seeing each other appear and disappear across chunks is covered by `ChunkInterestTests`; it was not repeated in the browser.

### 5. Economy

| Check | Result |
|:--|:--|
| Seed, clothing and furniture counters (purchase changes coins and items) | Pass |
| Carpenter counter (coop blueprint) | Pass |
| Blacksmith counter (page with the tool upgrades) | Pass |
| Market only at the trader's counter; the `M` key and the menu have no market elsewhere (ADR-034) | Pass |
| Market: list at the counter, another player buys, proceeds credited | Pass |
| Shipping bin sells the held stack | Pass |
| Trade between two players (items and coins) | Pass |
| Mail (sale message arrives, the Mail tab lists it) | Pass |

Observation, not scored: the market's price field is the price of the whole listing, but it is labelled only "coins" / "xu", next to a quantity field; a seller can read it as a price per item.

### 6. Progress

| Check | Result |
|:--|:--|
| Energy (tool use costs it, shown in Skills) | Pass |
| Eating (food restores energy) | Pass |
| Skills (XP grows from tilling and mining) | Pass |
| Crafting a chest | Pass |
| Furnace (placed, smelting started, bar collected after game time) | Pass |
| Tool upgrade (hoe to copper hoe: hand in, two game days, collect) | Pass |
| Mine floors (shaft to floor 1, floor 5 by elevator, stone and ore mined) | Pass |
| Elevator (offers the deepest floor and takes the player there) | Pass |

Not exercised: going down floor by floor by ladder (ladders come from broken stones at random).

### 7. Animals

| Check | Result |
|:--|:--|
| Commission a coop (blueprint, place, built after two game days) | Pass |
| Buy a chicken | Pass |
| Feed (hay in the bench), pet, collect an egg the next game day | Pass |

### 8. Social and co-op

| Check | Result |
|:--|:--|
| Local chat between two players | **Fail (fixed)**, F3 below |
| Chat profanity masked for the receiver | Pass |
| Friends (request, accept, both listed) | Pass |
| Visiting a farm (visitor can water; cannot till or plant, message shown) | Pass |
| Create a co-op, invite, accept (the invitation also arrives as mail) | Pass |
| Co-op land (members till inside the open block; outside it is refused) | Pass |
| Warehouse (deposit moves items into the stock) | Pass |
| Co-op upgrade (the next warehouse level is bought from the stock) | Pass |

### 9. Quests

| Check | Result |
|:--|:--|
| Tutorial chain (till, water, harvest, ship, buy seeds, chop, craft: tracker follows each step, 190 coins paid) | Pass |
| Town requests board (three requests listed, delivery pays the reward) | Pass |

### 10. Audio

| Check | Result |
|:--|:--|
| No `AudioContext` before the first click, one running after it, footsteps, tool swing and panel sounds play, no errors (`tests/browser/audio-check.js`, all four configurations; `audio-module-check.js`) | Pass |

## Failures

### F1. Game menu taller than a landscape phone viewport (open)

- **Seen:** Pixel 5 profile in landscape (802x293 CSS pixels, touch emulation). Guest start, press `I`. The Inventory window's bottom edge is at y=404 and the Skills window's at y=298,
  so the bottom of the grid and its hotbar row are off screen; the tab strip also overlaps the calendar clock.
- **Console:** none.
- **Screenshot (scratch):** `shots/i2t-pixel5-landscape-vi-menu.png`, `shots/i2t2-landscape-inv.png`.
- **Where:** `wasm/Nosum.Wasm.Controls/wwwroot/nosum-theme.js` `unitFor`: the pixel `--u` never goes below `MIN_UNIT = 2` and the menu is `MENU_UNITS_TALL = 204` units tall, so it needs
  408 px of height; the same floor is in `MapScene.ScaleFor` (the world zoom). Fixing it means either allowing `--u = 1` on short screens (a design decision about the pixel grid,
  ADR-016/ADR-017) or making the menu pages scroll inside a smaller window. Not a small local change.

### F2. Menu tab strip wider than a portrait phone viewport (open)

- **Seen:** Pixel 5 profile in portrait (393x727). Open the menu: the strip of 11 tabs ends at x=408, so the Settings tab is clipped at the right edge (the gear button still opens Settings).
- **Console:** none.
- **Screenshot (scratch):** `shots/i2t-pixel5-portrait-en-menu.png`.
- **Where:** `wasm/Nosum.Wasm.Controls/Menu/GameMenu.razor.css` `.menu-tabs` / `.menu-tab` (16 units wide, 2 units gap, `--u` at its floor of 2). The same pixel-floor question as F1; wrapping or scrolling the strip is the other option.

### F3. Closing the chat box raised a page error (fixed)

- **Seen:** open the chat with `Enter`, type, press `Enter` to send (or `Escape` to close). Each time the page raised `Assertion failed - heap is currently locked`.
- **Console:** `pageerror: Assertion failed - heap is currently locked` at `onInputBlur (chat-input.js:46)` through `invokeMethodAsync`.
- **Cause:** closing the box makes Blazor remove the focused input inside a render batch; the removal fires `blur`, and the blur handler called into .NET while the runtime had the heap locked.
- **Fix:** `chat-input.js` now makes the call after the batch (`setTimeout`). Test: `tests/browser/chat-check.js` (open, send, close with no errors).
- **Screenshot (scratch):** `shots/i8a-1280x720-en-chat.png`.

### F4. `AdvanceClock` missing from the tester build (fixed)

The `-p:NosumDevTools=true` build exported only `Grant`, so a tester could not skip game days from the console although `DevChannel.AdvanceClock` and the server tool exist.
`DevTools.AdvanceClock(days)` is now exported next to it (compiled only in that build).
