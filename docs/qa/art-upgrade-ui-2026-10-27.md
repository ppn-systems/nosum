# UI art redraw: before and after, 2026-10-27

Branch `feat/art-upgrade-ui`. Every shot is the real game in Chromium (Playwright) at the stated size, registered account, host in Development. "Before" is the client of `feat/art-upgrade-sprites` (the flat orange rectangles); "after" is this branch.

| Surface | 1280 x 720 before | 1280 x 720 after | 1920 x 1080 before | 1920 x 1080 after |
| :-- | :-- | :-- | :-- | :-- |
| HUD on the farm (quest card top left, clock, coins and gear top right, message line, hotbar and energy gauge) | [before](art-ui-before-hud-farm-1280x720.png) | [after](art-ui-after-hud-farm-1280x720.png) | [before](art-ui-before-hud-farm-1920x1080.png) | [after](art-ui-after-hud-farm-1920x1080.png) |
| Inventory open | [before](art-ui-before-inventory-1280x720.png) | [after](art-ui-after-inventory-1280x720.png) | [before](art-ui-before-inventory-1920x1080.png) | [after](art-ui-after-inventory-1920x1080.png) |
| Seed shop dialog (Buy at the seed merchant; before = the old counter panel) | [before](art-ui-before-shop-1280x720.png) | [after](art-ui-after-shop-1280x720.png) | [before](art-ui-before-shop-1920x1080.png) | [after](art-ui-after-shop-1920x1080.png) |
| Dialog window: the mine elevator picker | [before](art-ui-before-dialog-1280x720.png) | [after](art-ui-after-dialog-1280x720.png) | [before](art-ui-before-dialog-1920x1080.png) | [after](art-ui-after-dialog-1920x1080.png) |
| NPC dialog box (the seed merchant, PR #72; no before: it did not exist) | - | [after](art-ui-after-npc-dialog-1280x720.png) | - | [after](art-ui-after-npc-dialog-1920x1080.png) |
| Quest card, cropped from the HUD shot at native size | [before](art-ui-before-quest-card-1280x720.png) | [after](art-ui-after-quest-card-1280x720.png) | [before](art-ui-before-quest-card-1920x1080.png) | [after](art-ui-after-quest-card-1920x1080.png) |

The narrow-window overlay of the device gate (PR #71) is a `GamePanel`, so it wears the same frame: [after, 480 x 720](art-ui-after-narrow-window-480x720.png) (no before: the gate did not exist then).

## What changed

- The art is Kenney's CC0 UI packs (UI Pack - Pixel Adventure for the window and HUD frames, Pixel UI pack for the buttons, close button, tab plates and cursors), committed under `art/source/ui/` with their licenses and pinned by SHA-256; `tools/art_repack/ui.py` repacks them into the sheets (`repack_cc0_art.py --only ui --check` is green). Pieces the packs lack (slot bevel, selection frame, title strip, energy gauge, tab pictograms, coin, pager arrows, text field) are drawn in their style from one palette (`palette.py`); the palette block of `nosum-theme.css` is generated from it.
- One chunky frame per window: wood with steel corner brackets round a flat parchment page and a wood title strip. HUD boxes, the tooltip and the message line use the smaller wood plaque. Sections inside a window have no frame, only a thin rule under their heading.
- Slots: a 1 px inset bevel. The selected hotbar slot: a thick gold frame with a two-step glow, lifted 1 px. Slot numbers and stack counts on dark pills.
- Buttons, tabs, close button, gear: the pack's chunky blocks (wood primary, stone neutral, gold active tab, red danger) with a pressed frame, a lightened hover with a thin highlight border, and the label sinking 2 px when pressed. Toggles and slider knobs use the same bevel colours; the slider is a sunk groove.
- Energy gauge: framed, gold end caps, stepped gradient fill (red when low). Cursors: the pack's arrow and hand.

## How it was run

- PostgreSQL 16 `nosum_dev`; client published with `-p:NosumDevTools=true`; `server/Nosum.Host` with `DOTNET_ENVIRONMENT=Development` and `NOSUM_CALENDAR_EPOCH` set so the clock reads about 11:00; `python3 -m http.server` on port 5080.
- The account was placed by editing `player_states` while the host was stopped: farm for the HUD and inventory, Town (43, 57) for the seed shop, the mine entrance below the elevator for the dialog.

## Limits

- The trade and co-op invite dialogs share the dialog frame but need two players and were not shot.
- The mine dims the whole screen under an open dialog (the HUD backdrop), so the dialog shot is darker around the window in both versions.
- At 1920 x 1080 the menu tab row runs over the clock (unchanged from before).
- Hover and pressed states were checked in the browser earlier in this branch's history but are not in the committed shots.
