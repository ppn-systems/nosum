# Browser proof: NPC merchants and the menu split (ADR-043), 2026-10-27

A browser run of [ADR-043](../adr/ADR-043-npc-merchants-and-the-menu-split.md) on branch `feat/npc-merchants`: the merchants standing at their counters, the dialog
box, the shop, market, carpenter and board pages as standalone dialogs, and the menu without them. Screenshots are in [`npc-merchants/`](npc-merchants/).

## What was run

- Host: `DOTNET_ENVIRONMENT=Development dotnet run -c Release --project server/Nosum.Host` on PostgreSQL 16 (`nosum_dev`), restarted before each visit so the saved place is read
  from the database, with `NOSUM_CALENDAR_EPOCH` set so the game clock reads 11:00 (daylight).
- Client: `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`, served as static files on port 5080.
- Chromium through Playwright at 1280x720 (and 393x727 for the phone check), one **registered** account moved by editing its `player_states` row while the host was
  stopped (x = tile * 16 + 8, y = (tile + 1) * 16 - 1). The script is [`tests/browser/npc-merchants-check.js`](../../tests/browser/npc-merchants-check.js).
- The account language was switched on the settings page (Vietnamese, then English); both runs are in the screenshots.

## Result

| Check | Result |
|:--|:--|
| Carpenter standing beside his counter, farmer on the counter tile (`1-npc-at-counter-*`) | Pass in vi and en |
| Action key opens the dialog box with portrait, name, role, greeting and Buy / Build / Talk / Leave; the hotbar steps aside (`2-npc-dialog-*`) | Pass in vi and en |
| Buy opens the shop as a dialog of its own, with one title and no tabs (`3-shop-dialog-*`) | Pass in vi and en |
| Close button of the dialog goes back to the box; Build opens the buildings and animals page (`4-build-dialog-*`) | Pass in vi and en |
| Escape ends the conversation and brings the hotbar back | Pass |
| Menu (`I`) shows 11 player tabs and no shop, market, board, chest or furnace tab (`5-menu-*`) | Pass in vi and en |
| Mail icon in the HUD next to the cog opens the Mail tab (`6-mail-*`); the cog tip is only "Settings" | Pass in vi and en |
| Click on the carpenter opens his box; a click on empty ground does nothing | Pass (mouse) |
| Buying hay at the carpenter: 500 to 470 coins, "Bought. It is in your inventory." (`9-bought-hay-en`) | Pass |
| Seed shopkeeper and board keeper flank the seed shop's door (`7-town-square-en`); the seed keeper's box offers Buy / Talk and Buy opens the seed shop | Pass |
| Trader's box offers Buy / Sell / Talk; Sell opens the market on its Sell tab (`8-market-sell-en`) | Pass |
| Phone width 393x727: box and carpenter page fit (`10-phone-box-en`, `11-phone-build-dialog-en`) | Pass |
| Console errors, page errors, failed requests during all runs | None |

Art: the seven world sheets and portraits as generated, enlarged, in [`npc-merchants/0-art-contact-sheet.png`](npc-merchants/0-art-contact-sheet.png).

## Not run

- The tailor, furniture seller and blacksmith were not opened in the browser (their placement is checked by `NpcCatalogTests` on the Town map, their art and text by the
  coverage tests). The blacksmith's Upgrade button and the board keeper's Goals button were not clicked.
- A tap on a merchant with a finger was not tried; the click path is the same code as the mouse's.
- 1920x1080, the Vietnamese run of the town square and the market, and the mine and farm maps (no merchants there) were not re-run.
