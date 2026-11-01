# Grade: world-pass5 EN 390x844 (41 frames)

Source: /home/user/nosum/docs/qa/world-pass5/en/390x844/ (41 PNG; the brief said about 52, the folder holds 41). Audit: /home/user/nosum/docs/qa/world-pass5/audit-en-390x844.json.
Points: 1 region/menu column, 2 overlap, 3 button size, 4 edges, 5 panel width and title/close, 6 text fit, 7 scrollbar, 8 icons, 9 spacing, 10 English.
Rule: FAIL = wrong or missing step, unreachable panel, or unexplained missing HUD. PASS-WITH-NOTES = layout holds, minor issue or missing evidence. "n.a." = not applicable.
Global, all frames: no Vietnamese text seen (the "Tiếng Việt" button is the language name, which is correct). No raw keys seen. Point 7 ok everywhere. Point 8 ok (icons whole).

| frame | status | points failing | grade | note |
|:--|:--|:--|:--|:--|
| 01-settings-English | PASS | none | PASS | Full-width panel, title left, close right, English selected. |
| 02-HUD-start | FAIL | 1 (n.a. HUD) | FAIL | Frame is still the Settings window; no HUD start state captured. |
| 03-running-energy | PASS | none | PASS | Quest card, hotbar, Z/X/V keys, touch controls all placed. |
| 04-minimap-toggle-M | FAIL | evidence | FAIL | Identical to 03; no minimap visible, so the toggle outcome is not shown. |
| 05-farm-grant-seeds-sprinkler-rod | PASS | none | PASS | Items in hotbar; hint toast over the world only. |
| 06-farm-till-hoe-E | PASS | none | PASS | "Soil tilled." toast, quest advanced to Water. |
| 07-farm-sow-seeds-E | FAIL | 2, functional | FAIL | Sow fails in this season (audit); no sow result visible; hint toast overlaps joystick. |
| 08-farm-water-can-E | PASS-WITH-NOTES | 2 | PASS-WITH-NOTES | "Watered." shown but quest still 0/1; hint toast overlaps joystick. |
| 09-farm-sprinkler-place-E | PASS-WITH-NOTES | 2 | PASS-WITH-NOTES | "Placed." and "Watered." toasts; hint toast overlaps joystick. |
| 10-farm-advance-4-days-grow | PASS-WITH-NOTES | 2, evidence | PASS-WITH-NOTES | Rain, Day 5; hint toast overlaps joystick; no visible crop growth. |
| 11-farm-harvest-E-empty-hand | PASS-WITH-NOTES | 2, evidence | PASS-WITH-NOTES | Day 15; "Something is in the way."; hint overlaps joystick; no crop or harvest visible. |
| 12-farm-wither-no-water-10-days | FAIL | 1, functional | FAIL | No window open, yet hotbar, Chat/Run/Use absent; farm out of view, so wither not shown. |
| 13-merchant-seed-shop-dialog | PASS-WITH-NOTES | 3, 5 | PASS-WITH-NOTES | Buy/Talk/Leave widths uneven (Leave wider); no close X. |
| 14-shop-seed-shop-buy-list | PASS-WITH-NOTES | 6 | PASS-WITH-NOTES | "More" wraps alone to a second row; greyed out-of-season rows keep same Buy look. |
| 15-shop-seed-shop-quantity-stepper | PASS-WITH-NOTES | 6, evidence | PASS-WITH-NOTES | Identical to 14; x5/All state not visible; "More" wrap. |
| 16-merchant-clothing-shop-dialog | PASS-WITH-NOTES | 3, 5 | PASS-WITH-NOTES | Five buttons, uneven widths; no close X. |
| 17-shop-clothing-shop-buy-list | PASS-WITH-NOTES | 6 | PASS-WITH-NOTES | "More" wraps alone. |
| 18-shop-clothing-shop-quantity-stepper | PASS-WITH-NOTES | evidence | PASS-WITH-NOTES | Identical to 17; stepper state not shown. |
| 19-merchant-furniture-shop-dialog | PASS-WITH-NOTES | 3, 5 | PASS-WITH-NOTES | Same dialog pattern as 13. |
| 20-shop-furniture-shop-buy-list | PASS-WITH-NOTES | 6 | PASS-WITH-NOTES | "More" wraps alone; list fits, icons whole. |
| 21-shop-furniture-shop-quantity-stepper | PASS-WITH-NOTES | evidence | PASS-WITH-NOTES | Identical to 20; stepper state not shown. |
| 22-market-open | PASS-WITH-NOTES | evidence | PASS-WITH-NOTES | Panel and filters fit; "No listings found."; hotbar and touch controls hidden under window; no listing or buy path shown. |
| 23-fishing-cast | PASS | none | PASS | "Waiting for a bite..." toast clear of controls. |
| 24-fishing-bite-QA | PASS | none | PASS | Bite toast readable; gap between lines is a minor spacing note. |
| 25-fishing-reel-in-and-catch-card | PASS-WITH-NOTES | evidence | PASS-WITH-NOTES | "The fish got away!" (a miss); no catch card to grade. |
| 26-combat-Z-with-no-monster-in-reach-toast | PASS | none | PASS | Two toasts stacked cleanly. |
| 27-combat-teleport-next-to-a-monster-woods | PASS-WITH-NOTES | evidence | PASS-WITH-NOTES | Monster and damage readable; player name label not visible. |
| 28-combat-Z-with-a-monster-in-reach-cooldow | PASS | none | PASS | Z cooldown visible; HUD clear. |
| 29-combat-X-fireball-monster-in-reach | PASS-WITH-NOTES | 2 (minor) | PASS-WITH-NOTES | "CRIT -18" float touches quest card bottom edge. |
| 30-combat-V-heal-at-full-health-toast | PASS | none | PASS | "Not enough MP to cast!" and combo chip clear of each other. |
| 31-menu-phone-crafting-friends-skills-in-th | PASS-WITH-NOTES | 1 (note) | PASS-WITH-NOTES | Menu grid clean, 10 tiles, all labels; Menu opens from an icon-only button (no text label). |
| 32-crafting-panel-fail | FAIL | functional | FAIL | Shows Menu, not Crafting; Crafting tile visible but click fails "Element is not visible". |
| 33-panel-mail | PASS | none | PASS | Title left, close right, text fits. Bottom HUD hidden while open (see Issues). |
| 34-panel-friends-fail | FAIL | functional | FAIL | No Friends panel; click fails "Element is not visible". |
| 35-panel-skills-fail | FAIL | functional | FAIL | No Skills panel; click fails. |
| 36-panel-crafting-fail | FAIL | functional | FAIL | No Crafting panel; click fails. |
| 37-panel-co-op-warehouse-G | PASS-WITH-NOTES | 3, evidence | PASS-WITH-NOTES | Co-op name input (about 30 px) taller than "Found" button; no warehouse list shown. |
| 38-panel-profile-rename-N | FAIL | 3, functional | FAIL | No rename control visible next to nickname; "Save progress" and "Sign out" differ in width and style. |
| 39-calendar-season-passes-QA | PASS-WITH-NOTES | 2 (note) | PASS-WITH-NOTES | NEW SEASON banner correct; a "Settings" hover tooltip stays on screen (touch device). |
| 40-NPC-dialogue-board-keeper | PASS-WITH-NOTES | 3, 5, evidence | PASS-WITH-NOTES | Shows Mai, Seed shopkeeper, not a board keeper; buttons uneven; no close X. |
| 41-level-up-banner-QA | PASS-WITH-NOTES | 9 (note) | PASS-WITH-NOTES | "LEVEL UP!" text repeated in banner, world float, toast and chip; the "19" label sits on the bar. |

Totals: PASS 10, PASS-WITH-NOTES 22, FAIL 9 (41 frames).

## Issues (most severe first)

1. Crafting, Friends and Skills panels unreachable in this pass. Frames 32, 34, 35, 36 (`.../390x844/32-crafting-panel-fail.png`, `34-panel-friends-fail.png`, `35-panel-skills-fail.png`, `36-panel-crafting-fail.png`) show no panel. Frame 32 shows the Menu with the Crafting tile visible, yet the click reports "Element is not visible". Either the tile is not hit-testable or the harness is off. Check the menu tile wiring. Point 1 / unreachable panel.
2. Profile rename control missing. `38-panel-profile-rename-N.png` shows "Current nickname: Guest 8992" with no rename button, and the once-a-day note the audit lists is absent. Audit says captured; the frame does not show it.
3. Sow step fails. `07-farm-sow-seeds-E.png` shows no sow result; audit records "That crop does not grow in this season" and a season change mid-run (Summer to Fall). Functional, and the step is not verified.
4. HUD missing with no window open. `12-farm-wither-no-water-10-days.png` has no hotbar, no Chat/Run/Use and no hint toast; the farm is out of view so the wither result cannot be checked. Confirm whether this is a transition or a real hide.
5. Hint toast overlaps the joystick. `07`, `08`, `09`, `10`, `11` (for example `08-farm-water-can-E.png`): the toast starts at about x 85 over the joystick ring at about x 17-93. Point 2 (touch controls).
6. Quest objective does not tick. `08-farm-water-can-E.png` shows "Watered." yet the card still reads "Water a patch of soil 0/1"; the same card in 09 to 41 never completes. Verify the objective update.
7. Crop state not visible. `10-farm-advance-4-days-grow.png` and `11-farm-harvest-E-empty-hand.png` show the same plot with the sprinkler and no visible crop, so growth and harvest cannot be confirmed from the frames.
8. Shop quantity steppers not shown. `15-shop-seed-shop-quantity-stepper.png`, `18-...`, `21-...` are pixel-identical in layout to `14`, `17`, `20`; x5 and All are not visible. The audit's "x5 yes" is not backed by the frames.
9. "More" button wraps alone in shop headers. `14-shop-seed-shop-buy-list.png`, `17-shop-clothing-shop-buy-list.png`, `20-shop-furniture-shop-buy-list.png`: the row "x1 x5 All Less [1]" fits, then "More" drops to its own row at the left. Point 6 / 9.
10. Capture labels do not match frames. `02-HUD-start.png` is the Settings window, not a HUD start. `04-minimap-toggle-M.png` is identical to 03, with no minimap visible. `40-NPC-dialogue-board-keeper.png` shows Mai, Seed shopkeeper. `37-panel-co-op-warehouse-G.png` shows the co-op name form, not a warehouse list.
11. Button widths and styles inconsistent. Dialog buttons are content-sized and uneven (`13-merchant-seed-shop-dialog.png`, `16-merchant-clothing-shop-dialog.png`, `19-...`, `40-...`). `38-...` has "Save progress with an account" and "Sign out" in different widths and styles. `37-...` has the co-op input about 30 px tall against a shorter "Found" button. Point 3.
12. No close control on NPC dialogs. `13`, `16`, `19`, `40`: the title row has the name only; "Leave" is the only exit. Point 5, check against ui-standard.
13. Bottom HUD hidden while windows are open. `22-market-open.png`, `31-menu-...`, `33-panel-mail.png`, `37-...`, `38-...` show no hotbar and no touch controls. Consistent with a modal layout, but confirm it matches ui-standard.
14. Quest card header low contrast. Light grey title on pale grey bar in all frames with the quest card (`03-running-energy.png` onward). Readable but weak.
15. Menu opens from an icon-only top-right button with no "Menu" label (`31-menu-...`, and the top-right cluster in most HUD frames). Point 1 asks for a "Menu" button.
16. Minor: level-up text repeats four times (`41-level-up-banner-QA.png`); a "Settings" hover tooltip stays visible on touch (`39-calendar-season-passes-QA.png`); the "CRIT -18" float touches the quest card (`29-combat-X-fireball-monster-in-reach.png`).
