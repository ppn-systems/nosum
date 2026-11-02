# EN 1280x720 QA grade (world-pass5)

Scope: 40 PNG frames in docs/qa/world-pass5/en/1280x720/ (the directory holds 40 files, not about 52). All 40 viewed. Audit: docs/qa/world-pass5/audit-en-1280x720.json (52 rows; 40 point at a screenshot here; 10 rows not exercised).
Checklist: docs/design/ui-standard.md section 7, points 1-10. "state" = wrong or unproven state, not a layout point. Status = recorded audit status.

Totals: PASS 10 · PASS-WITH-NOTES 20 · FAIL 10

| frame | status | points failing | grade | note |
|:--|:--|:--|:--|:--|
| 01-settings-English | pass | none (3, 8 n.a.) | PASS | Settings panel: title left, X right; English selected. "Tiếng Việt" is the language's own name, not a leak. |
| 02-HUD-start | captured | state | FAIL | Pixel-identical to 01: Settings is still open, so this is not a HUD start frame. |
| 03-running-energy | captured | none | PASS-WITH-NOTES | Energy/HP/MP bars unlabelled, so the drain cannot be read from one frame. |
| 04-minimap-toggle-M | pass | state | FAIL | Minimap card still visible after M. Only pixels that differ from 03 are at the right edge (x 1256-1272). Audit "minimap height -> -1" is not shown. |
| 05-farm-grant-seeds-sprinkler-rod | captured | 6 | PASS-WITH-NOTES | Hint box wraps to leave "you." alone on line 2. Hotbar icons whole. |
| 06-farm-till-hoe-E | pass | 1 | PASS-WITH-NOTES | Floating "-3" sits on the top edge above the calendar card. |
| 07-farm-sow-seeds-E | pass | none | PASS | Two toasts stack cleanly; slot 3 count 17. |
| 08-farm-water-can-E | pass | none | PASS | Watering can count 39; toasts fine. |
| 09-farm-sprinkler-place-E | captured | none | PASS-WITH-NOTES | "Placed." toast and slot 9 emptied. Placed sprinkler is an unlabelled dark block; no crop sprite visible. |
| 10-farm-advance-4-days-grow | captured | state | FAIL | Winter Day 5 after 4 days, but no crop sprite on the plot (zoom: identical to 12). Hint banner still says "Plant a seed..." although Plant a seed is 1/1. |
| 11-farm-harvest-E-empty-hand | captured | state | FAIL | "Something is in the way." Harvest refused; Harvest a crop stays 0/1. |
| 12-farm-wither-no-water-10-days | captured | state | FAIL | Winter Day 15. No grown or withered crop visible, so the wither claim is unproven. Same refusal toast. |
| 13-merchant-seed-shop-dialog | pass | none | PASS | Mai dialog; Buy / Talk / Leave fit. |
| 14-shop-seed-shop-buy-list | pass | 6 | FAIL | Clipped text: "Plants in Sprin..." (Radish, Cauliflower). "Balance: 1,520 / coins" wraps. Minimap and menu column absent while the shop is open. |
| 15-shop-seed-shop-quantity-stepper | captured | state | FAIL | Byte-identical to 14: x1 selected, qty 1. The audit's "x5 yes; All yes" is not shown. |
| 16-merchant-clothing-shop-dialog | pass | 4, 9 | PASS-WITH-NOTES | Button row wraps: "Leave" sits alone on row 2. Body line gap is about 2x the single-line dialogs (13, 39). |
| 17-shop-clothing-shop-buy-list | pass | 6 | PASS-WITH-NOTES | "Balance: 1,520 / coins" wraps. Otherwise clean, one-column grid aligned. |
| 18-shop-clothing-shop-quantity-stepper | captured | state | FAIL | Differs from 17 only in the selected-item tile. x5 and All not shown; qty still 1. |
| 19-merchant-furniture-shop-dialog | pass | 9 | PASS-WITH-NOTES | Body line gap, as in 16. |
| 20-shop-furniture-shop-buy-list | pass | 6 | PASS-WITH-NOTES | "Balance: 1,520 / coins" wraps. Icons whole. |
| 21-shop-furniture-shop-quantity-stepper | captured | state | FAIL | Same as 18: differs from 20 only in the selected tile. Stepper change not shown. |
| 22-market-open | captured | none | PASS | Browse / Sell / My listings; search; sort tabs; "No listings found." Buy and list not exercised. |
| 23-fishing-cast-at-the-shore | pass | none | PASS | "Waiting for a bite..." toast; rod in slot 9. |
| 24-fishing-bite-QA | captured | 2 | PASS-WITH-NOTES | Bite "!" bubble touches the "Guest 6442" name label. |
| 25-fishing-reel-in-and-catch-card | captured | state | PASS-WITH-NOTES | Miss ("The fish got away!"). No catch card, so the catch path is not verified. |
| 26-combat-Z-no-monster-in-reach | pass | none | PASS | Stale "fish got away" toast stacks above the Z toast; acceptable queue. |
| 27-combat-teleport-next-to-a-monster | captured | state | FAIL | No monster beside the farmer. Nearest mushroom monster is about 5 tiles away, so the scenario is not shown. |
| 28-combat-Z-monster-in-reach | pass | 2 | PASS-WITH-NOTES | "CRIT -18" damage text stacks under "Guest 6442" at the top-left of the calendar card. |
| 29-combat-X-fireball | captured | 1 | PASS-WITH-NOTES | "2x COMBO!" sits above the top HUD row (y 13). "Combo 2x (+5%)" tooltip floats over the world above Z/X/V. |
| 30-combat-V-heal-no-MP | pass | 1 | PASS-WITH-NOTES | Toast correct; the Combo tooltip floats over the world; MP gauge empty. |
| 31-crafting-panel | captured | 4 | PASS-WITH-NOTES | Recipe grid starts at x 410 while the "Recipes" heading starts at x 334. Black badges straddle slot edges. |
| 32-panel-mail | captured | 1 | PASS-WITH-NOTES | Stale "2x COMBO!" over the top HUD edge. Panel itself correct (empty mailbox text). |
| 33-panel-friends | captured | none | PASS | Guest notice box, Blocked players, Block. Title left, X right. |
| 34-panel-skills | captured | none | PASS | Skills and Monster bestiary tabs; HP/MP/Energy bars labelled; skill rows aligned. |
| 35-panel-crafting | captured | 4 | PASS-WITH-NOTES | Panel identical to 31 (only world behind differs). Duplicate capture. |
| 36-panel-co-op-warehouse-G | captured | state | PASS-WITH-NOTES | G opens the co-op founding form ("Found a co-op"), not a warehouse. Warehouse not verified. |
| 37-panel-profile-rename-N | captured | state | PASS-WITH-NOTES | Guest profile; no rename control or once-a-day note shown. "Save progress..." label fits. |
| 38-calendar-season-passes-QA | pass | 1, 2, 4 | PASS-WITH-NOTES | NEW SEASON banner is centred near x 829, not on the screen (640) like LEVEL UP. "Crafting" hover tooltip covers the top menu button and runs to x 1275, past the HUD edge (1255). |
| 39-NPC-dialogue-board-keeper | pass | none | PASS | Mai dialog, single line; fits. |
| 40-level-up-banner-QA | pass (layout row FAIL) | 2 | PASS-WITH-NOTES | Audit "covers furniture-dock" not reproduced: banner x 507-775, panel ends x 495. "Level 2!" tag touches the HP "25" tag. Furniture panel stays open through the knock-out toast. |

## Issues (most severe first)

1. Farm loop is not functional in the captures (FAIL 10, 11, 12). No grown or withered crop is visible on the plot after 4 and 10 days. Harvest returns "Something is in the way." and the quest stays 0/1. Evidence: /home/user/nosum/docs/qa/world-pass5/en/1280x720/10-farm-advance-4-days-grow.png, 11-farm-harvest-E-empty-hand.png, 12-farm-wither-no-water-10-days.png. Zoom of the plot in 10 and 12 is identical.
2. Wrong-state captures (FAIL 02, 15, 18, 21). 02 shows Settings, not HUD start (byte-identical to 01). 15 is byte-identical to 14. 18 and 21 differ from 17 and 20 only by the selected item tile. The stepper states the audit marks "x5 yes; All yes" are not in the images.
3. Minimap still visible after M (FAIL 04). Compared with 03, the only changed pixels are at x 1256-1272, y 369-402.
4. Clipped text in the seed shop (FAIL 14). "Plants in Sprin..." is truncated for Radish seeds and Cauliflower seeds. Evidence: 14-shop-seed-shop-buy-list.png.
5. Teleport scenario has no monster in frame (FAIL 27). The farmer is about 5 tiles from the nearest monster. Evidence: 27-combat-teleport-next-to-a-monster-woods.png.
6. Co-op frame does not show the warehouse (36). G opens the founding form for a guest; the warehouse list the audit expects is absent.
7. Banner placement inconsistent (38 vs 40). NEW SEASON is off-centre (x 720-938); LEVEL UP is centred (x 507-775).
8. Minimap and menu column vanish whenever a panel, shop or Settings is open (01, 02, 14, 15, 17, 18, 20-22, 31-37, 40). They stay for NPC dialogues (13, 16, 19, 39). Pixel check of x 1208-1256 confirms it.
9. Hover tooltip covers a menu button and overhangs the HUD edge (38). Evidence: 38-calendar-season-passes-QA.png (zoom at x 1140-1280, y 200-290).
10. Recipe grid not aligned to section headings (31, 35). Grid starts x 410; headings start x 334; grid is centred, not left-aligned.
11. Shop "Balance: 1,520 / coins" wraps onto two lines (14, 17, 20). Label should fit one line.
12. Dialog button row wraps; "Leave" orphaned (16). Body line gap in 16 and 19 is larger than in 13 and 39.
13. Combo banner and tooltip sit outside HUD regions (29, 30, 32). "2x COMBO!" is above the top row; the Combo tooltip floats over the world.
14. Unlabelled top-right sprite under the gear (about x 1232, y 103) appears in frames with the minimap hidden (01, 13-15, 17, 18, 20-22, 31-37, 40). Not in the top-right row or the menu column; its meaning is not shown.
15. Hint box wraps with a one-word last line "you." (05).
16. Gauges unlabelled; a bare "9" (38) and "25" (40) tag sits above the HP gauge, and a purple bar with no label sits below the gauges (38). Meaning not visible. Evidence: zoom of 38 gauges.
17. Audit FAIL for 40 ("covers furniture-dock") is not reproduced in the frame; banner and panel do not overlap.
18. Not captured: catch card (25 is a miss), rename control (37 shows guest profile only), stepper states (15, 18, 21), animals, pets, crafting at stations, market list/buy, mail send, co-op deposit, backpack, rename scroll, shipping bin, dungeon/mine (10 audit rows "not exercised").
