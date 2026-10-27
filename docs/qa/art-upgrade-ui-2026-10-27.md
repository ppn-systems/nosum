# UI art redraw: before and after, 2026-10-27

Branch `feat/art-upgrade-ui`. Every shot is the real game in Chromium (Playwright) at the stated size, registered account, host in Development. "Before" is the client of `feat/art-upgrade-sprites` (the flat orange rectangles); "after" is this branch.

| Surface | 1280 x 720 before | 1280 x 720 after | 1920 x 1080 before | 1920 x 1080 after |
| :-- | :-- | :-- | :-- | :-- |
| HUD on the farm (quest card top left, clock, coins and gear top right, message line, hotbar and energy gauge) | [before](art-ui-before-hud-farm-1280x720.png) | [after](art-ui-after-hud-farm-1280x720.png) | [before](art-ui-before-hud-farm-1920x1080.png) | [after](art-ui-after-hud-farm-1920x1080.png) |
| Inventory open | [before](art-ui-before-inventory-1280x720.png) | [after](art-ui-after-inventory-1280x720.png) | [before](art-ui-before-inventory-1920x1080.png) | [after](art-ui-after-inventory-1920x1080.png) |
| Seed shop open (Town counter) | [before](art-ui-before-shop-1280x720.png) | [after](art-ui-after-shop-1280x720.png) | [before](art-ui-before-shop-1920x1080.png) | [after](art-ui-after-shop-1920x1080.png) |
| Dialog: the mine elevator picker (the game has no NPC speech box; this is its dialog window) | [before](art-ui-before-dialog-1280x720.png) | [after](art-ui-after-dialog-1280x720.png) | [before](art-ui-before-dialog-1920x1080.png) | [after](art-ui-after-dialog-1920x1080.png) |
| Quest card, cropped from the HUD shot at native size | [before](art-ui-before-quest-card-1280x720.png) | [after](art-ui-after-quest-card-1280x720.png) | [before](art-ui-before-quest-card-1920x1080.png) | [after](art-ui-after-quest-card-1920x1080.png) |

## What changed

- One palette (`tools/art_repack/palette.py`), built from the tileset hues; the palette block of `nosum-theme.css` is generated from it (`repack_cc0_art.py --only ui --check` is green).
- Windows: rounded wood frame with a 1 px outline, lit top-left and shaded bottom-right bevel, a rivet per corner, a tiling parchment page and a wood title strip. HUD widgets, the tooltip and the message line use a smaller frame of the same kind; sections are sunk cards.
- Hotbar and slots: sunk bevelled slots, the selected slot in a gold frame with a two-step glow, slot numbers and stack counts on dark pills.
- Buttons, tabs, close button, gear, toggles and slider knobs: chunky blocks (outline, bright 2 px top and left edge, dark 2 px bottom and right edge, flat textured face; wood primary, stone neutral, gold active tab, red danger); hover lightens the face with a thin highlight border, pressed flattens the bevel and sinks 2 px. The twelve tab pictograms are redrawn as outlined two-tone pictures.
- Energy gauge: framed, gold end caps, stepped gradient fill (red when low). Progress bars: stepped gradient on a dark track. Cursors: outlined, light face with a shaded edge.

## How it was run

- PostgreSQL 16 `nosum_dev`; client published with `-p:NosumDevTools=true`; `server/Nosum.Host` with `DOTNET_ENVIRONMENT=Development` and `NOSUM_CALENDAR_EPOCH` set so the clock reads about 11:00; `python3 -m http.server` on port 5080.
- The account was placed by editing `player_states` while the host was stopped: farm for the HUD and inventory, Town (43, 57) for the seed shop, the mine entrance below the elevator for the dialog.

## Limits

- There is no NPC speech box in the client yet; the dialog row shows the elevator window, the one `GamePanel Class="dialog"` that can be opened by one player. The trade and co-op invite dialogs share its frame but need two players.
- The mine dims the whole screen under an open dialog (the HUD backdrop), so the dialog shot is darker around the window in both versions.
- At 1920 x 1080 the menu tab row runs over the clock (unchanged from before).
- Hover and pressed states were checked in the browser (a hovered and a pressed seed-shop button) but are not in the committed shots.
