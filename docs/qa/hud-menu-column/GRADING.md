# HUD menu column: before and after, graded against ui-standard §7

Scope: the menu column and the Menu window on `origin/master` at `6f3784a` (after), against the same HUD at `509fc99` (before, the state
just before PR #244). Both phases: vi and en, at 1280×720, 1920×1080 and 390×844 (touch). The chest page (bag and chest XL panel) is in both.

Captures: `before/<lang>/<viewport>/` (00-hud, 01-inventory, 02-crafting, 03-chest), `after/<lang>/<viewport>/` (the same plus 04-menu, the
Menu window). Runs: `run-before.txt`, `run-after.txt`. Column labels at 1280×720 and 390×844: `before/dom-buttons.txt`.

## What changed between the two

- Before: 15 icon buttons in the top right. Mail and Settings in the top row, then 13 buttons in two columns of seven and six (the drift in
  `docs/design/critique-inventory-crafting.md` F5).
- After: the column is one column of eight buttons under the minimap, right-aligned with it: seven panels (Inventory, Crafting, Trade, Friends,
  Skills, World map, Journal) and the Menu button. The other panels (Visitors, Pet, Profile) are tiles of the Menu window; Wardrobe and Co-op
  are merchant doors with no tile (`NpcKinds`). On a phone the column collapses into the Menu button and the window gives a tile for every panel.

## Grading (ui-standard §7)

| # | Check | After (1280×720 / 1920×1080 / 390×844) | Result |
| :-- | :-- | :-- | :-- |
| 1 | Every element in its region; no floating card over the world | Column and minimap in the top-right region, Menu window in the centre, no new card over the world. | Yes |
| 2 | Nothing overlaps (regions, panels, toasts, hotbar, touch) | 1280×720: column bottom clears the hotbar (column x ≥ 1205, hotbar x ≤ 920). 1920×1080: same. 390×844: the column is the Menu button; joystick and hotbar are clear. | Yes |
| 3 | One column, at most 8 buttons, same size and frame | 8 buttons, one column, all 16 u (14 u on a phone). | Yes |
| 4 | Edges line up: top-right row and minimap card share the right edge | Minimap and column share the right edge at all three sizes (1920×1080 closest measured). | Yes |
| 5 | Panels use `GamePanel` with S/M/L/XL and the shared title and close | Chest is XL with the shared title bar and close button; the Menu window and the inventory use the same frame. | Yes |
| 6 | Three font sizes only; no word wrap; nothing clipped | "Hồ sơ" (Profile) in the vi Menu tile sits on a narrow tile and needs a closer look at full resolution; the en tiles fit on one line. The key hint sits under the label. | Needs a closer look |
| 7 | No inner scrollbar at 1280×720 and above | The Menu window and the chest page fit without a scrollbar at 1280×720. | Yes |
| 8 | Item icons whole and centred | Hotbar, bag and chest slots show whole icons, centred. | Yes |
| 9 | Spacing in `--u` multiples and gap tokens | Not verifiable from screenshots; checked by reading the CSS, not by these captures. | Not checked here |
| 10 | Vietnamese and English both fit | vi and en captured at all three sizes; the en Menu tiles fit at 390×844; the vi chest and HUD fit at 1280×720. | Yes, except the item 6 check |

Notes that are not §7 items:

- The Menu window on desktop shows only the moved panels (Visitors, Pet, Profile) and no emote tile; the emote wheel is opened by its own
  control, which this capture does not cover.
- At 390×844 the DOM still holds the seven column buttons (hidden by CSS) and a minimap button; the screenshots show neither.
- The world label "Bus Stop" sits next to the column at 1920×1080; it is an engine label, not HUD.

## Result

Ten items: nine yes or not applicable, one (item 6) needs a closer look on the vi "Hồ sơ" tile. The column itself passes items 1 to 5
and 7 to 8 and 10 at every size captured. No layout change is needed on this evidence; the open item is a text check, not a fix.
