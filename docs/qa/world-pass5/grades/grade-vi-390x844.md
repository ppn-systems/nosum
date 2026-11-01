# QA grade: Nosum VI, 390x844 (world-pass5)

Frames: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/` (40 PNG files; the brief said about 50, the directory holds 40). Audit: `/home/user/nosum/docs/qa/world-pass5/audit-vi-390x844.json`. Read-only; no repo files changed.

Checklist points (ui-standard.md sections 6 and 7): 1 region / phone menu collapse; 2 overlap; 3 same-size button groups; 4 edges; 5 panel width and title bar; 6 text fit / wrap; 7 inner scrollbar; 8 icons; 9 spacing; 10 language. T = touch target under about 42 px. F = functional problem or unreachable panel.

Grade rule: FAIL = a section 6 violation, an unreachable panel, or a control covered or unusable. PASS-WITH-NOTES = layout correct, cosmetic or minor points only. PASS = every point ok.

## Frame table

| frame | status (audit) | points failing | grade | note |
|:--|:--|:--|:--|:--|
| 01-HUD-start.png | captured | 1, 2, 4 | FAIL | Two stacked column buttons top-right, no single Menu button. Heart icon sits over the bar caps. Quest card left edge (x~10) does not match the time pill (x~48). |
| 02-running-energy.png | captured | 1, 2 | FAIL | Column still present. Heart over bars. Energy drain not readable from a still frame. |
| 03-minimap-toggle-M.png | n/a | 1 | FAIL | No minimap and no map button visible. The audit says a map button exists; I cannot see it. Column present. |
| 04-farm-grant-seeds-sprinkler-rod.png | captured | 1, 2 | FAIL | Column; heart over bars. Items arrive in the hotbar, slot 9 filled. |
| 05-farm-till-hoe-E.png | pass | 1, 2 | FAIL | Audit pass overridden: column and heart. Toast fine. |
| 06-farm-sow-seeds-E.png | pass | 1, 2 | FAIL | Column and heart. Two toasts stack cleanly. |
| 07-farm-water-can-E.png | pass | 1, 2 | FAIL | Column and heart. Floating "-3" fine. |
| 08-farm-sprinkler-place-E.png | captured | 1, 2 | FAIL | Column and heart. Placed sprinkler dome peeks out above the hotbar top edge. |
| 09-farm-advance-4-days-grow.png | captured | 1, 2 | FAIL | Column and heart. Tutorial hint box covers the top-right corner of the joystick. |
| 10-farm-harvest-E-empty-hand.png | captured | 1, 2, F | FAIL | Column; hint box over joystick. Unripe harvest shows "Có vật cản ở chỗ này." (obstacle here), which is misleading. |
| 11-farm-wither-no-water-10-days.png | captured | 1, 2, F | FAIL | Same as 10. Withered state is hard to read at this size. |
| 12-merchant-seed-shop-dialog.png | pass | 1, T | FAIL | Column visible. Mua / Trò chuyện / Tạm biệt about 27 px tall. Dialog text fits. |
| 13-shop-seed-shop-buy-list.png | pass | 1, 6, 9, T | FAIL | Column. "Thêm" wraps onto its own line under the stepper row. Mua buttons about 25 px. Title bar and close fine. |
| 14-shop-seed-shop-quantity-stepper.png | captured | 1, 6, T, F | FAIL | Same as 13. Stepper shows x1 still active, visually identical to 13. The x5 / Tất cả result is not shown. |
| 15-merchant-clothing-shop-dialog.png | pass | 1, 6, T | FAIL | Column. Five-button row fits. Dialog text wraps with orphan "nhé.". |
| 16-shop-clothing-shop-buy-list.png | pass | 1, T | FAIL | Column. List, icons and edges fine. |
| 17-shop-clothing-shop-quantity-stepper.png | captured | 1, 6, T, F | FAIL | Same as 13. x5 result not shown. |
| 18-merchant-furniture-shop-dialog.png | pass | 1, T | FAIL | Column. Dialog fine. |
| 19-shop-furniture-shop-buy-list.png | pass | 1, 6, T | FAIL | Column. "Thêm" wraps under the row. |
| 20-shop-furniture-shop-quantity-stepper.png | captured | 1, 6, T, F | FAIL | Same as 13. x5 result not shown. |
| 21-market-open.png | captured | 1, T | FAIL | Column. Market tabs and filter buttons about 20 px. Empty state fits. Title bar fine. |
| 22-fishing-cast-at-the-shore-rod-face-sea-E.png | pass | 1, 2 | FAIL | Column and heart. Cast status toast fine. |
| 23-fishing-bite-QA.png | captured | 1, 2 | FAIL | Column and heart. Bite toast fits. |
| 24-fishing-reel-in-and-catch-card.png | captured | 1, 2 | FAIL | Column and heart. Miss toast. No catch card, which is expected for a miss. |
| 25-combat-Z-with-no-monster-in-reach-toast.png | pass | 1, 9 | FAIL | Column. Stale "Cá đã thoát mất!" toast stays above the new one. |
| 26-combat-teleport-next-to-a-monster-woods.png | captured | 1, F | FAIL | Column. No monster visibly beside the farmer; only the "HUYẾT THỆ" label, far left. Cannot verify. |
| 27-combat-Z-with-a-monster-in-reach-cooldow.png | pass | 1, 9 | FAIL | Column. Floating "-8" overlaps the "Khách 2347" name plate. Z greyed for cooldown (fine). |
| 28-combat-X-fireball-monster-in-reach.png | captured | 1, F | FAIL | Column. No fireball identifiable. "CHÍ MẠNG -34" overlaps the name plate. Combo toast sits right above the hotbar. |
| 29-combat-V-heal-at-full-health-toast.png | pass | 1, 9 | FAIL | Column. Two toasts nearly touching. Vietnamese text fits. |
| 30-menu-phone-crafting-friends-skills-in-th.png | pass | 1, 2, 6, 10 | FAIL | Menu window good: all 10 tiles, title bar, close. Dark band hides hotbar. Title "Menu" is English. "Bản đồ thế giới" wraps to two lines in its tile. |
| 31-crafting-panel-fail.png | FAIL | F, 2 | FAIL | Clicking Chế tạo did not open crafting; Menu still open. Dark band. Audit: "Element is not visible". |
| 32-panel-mail.png | captured | 1, 2 | FAIL | Mail window itself is good (title, close, text fits, full width). Column visible. Dark band hides hotbar. |
| 33-panel-friends-fail.png | FAIL | F | FAIL | Friends panel did not open; world and hotbar visible. Unreachable in this pass. |
| 34-panel-skills-fail.png | FAIL | F | FAIL | Skills panel did not open. |
| 35-panel-crafting-fail.png | FAIL | F | FAIL | Crafting panel did not open. |
| 36-panel-co-op-warehouse-G.png | captured | 1, 2, T | FAIL | Co-op window good (title, close, edges, text fits). "Lập" about 24 px. Dark band. Warehouse list not shown (only an empty invite list). |
| 37-panel-profile-rename-N.png | captured | 2, F | FAIL | Text fits and Đăng xuất reads as a danger action. No rename control visible, though the audit notes one. Dark band. |
| 38-calendar-season-passes-QA.png | pass | 1 | FAIL | Season banner fine. Column visible. |
| 39-NPC-dialogue-board-keeper.png | pass | 1, T, 6 | FAIL | Column. Dialog buttons about 27 px. "ấm đã." orphan wrap. |
| 40-level-up-banner-QA.png | pass | 1, 2, 9 | FAIL | Column; heart. Four level-up messages at once. Small "Thăng cấp 2!" toast sits right on top of hotbar slot 9. Floating text over name plate. |

Counts: PASS 0, PASS-WITH-NOTES 0, FAIL 40. Audit "pass" was overridden on 05, 06, 07, 12, 13, 16, 18, 22, 25, 27, 29, 30, 38, 39 and 40 because of the column (point 1). If issue 1 is set aside, 11 frames stay FAIL (09, 10, 11, 30, 31, 32, 33, 34, 35, 36, 37), and the other 29 would be PASS-WITH-NOTES.

## Issues (most severe first)

1. **Phone menu column not collapsed (section 6, point 1).** Two stacked icon buttons sit under the mail and settings icons at top-right, with no single Menu button. Visible in every HUD frame. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/01-HUD-start.png`, also `38-calendar-season-passes-QA.png`, `40-level-up-banner-QA.png`.
2. **Crafting, Friends and Skills panels unreachable (F).** Menu tiles are present, but clicking Chế tạo leaves the Menu open and no panel appears. Friends and Skills show no panel either. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/31-crafting-panel-fail.png`, `33-panel-friends-fail.png`, `34-panel-skills-fail.png`, `35-panel-crafting-fail.png`.
3. **Dark band hides the hotbar and touch controls when a window is open.** From about y=715 to the bottom, the frame is opaque dark navy. Only the heart icon floats in it. The hotbar, Z/X/V and the joystick are gone. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/30-menu-phone-crafting-friends-skills-in-th.png`, `32-panel-mail.png`, `36-panel-co-op-warehouse-G.png`, `37-panel-profile-rename-N.png`.
4. **Tutorial hint box overlaps the joystick (point 2).** The box's left edge (x~85) runs into the joystick ring's top-right (x~95, y~600). Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/09-farm-advance-4-days-grow.png`, `10-farm-harvest-E-empty-hand.png`, `11-farm-wither-no-water-10-days.png`.
5. **Hotbar bars sit outside the hotbar frame, heart floats over them (point 2).** Health, mana and stamina bars are outside the hotbar panel. The heart icon overlaps the top of the green bar. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/01-HUD-start.png` (zoomed), `22-fishing-cast-at-the-shore-rod-face-sea-E.png`, `40-level-up-banner-QA.png`.
6. **Shop "Thêm" wraps onto its own row (points 6 and 9).** It sits alone under the x1 / x5 / Tất cả / Bớt / qty row. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/13-shop-seed-shop-buy-list.png`, `16-shop-clothing-shop-buy-list.png`, `19-shop-furniture-shop-buy-list.png`.
7. **Touch targets under about 42 px (T).** Dialog buttons Mua / Trò chuyện / Tạm biệt are about 27 px tall. Shop stepper and Mua buttons are about 25 px. Market tabs and filters are about 20 px. Co-op "Lập" is about 24 px. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/12-merchant-seed-shop-dialog.png`, `21-market-open.png`, `36-panel-co-op-warehouse-G.png`, `39-NPC-dialogue-board-keeper.png`.
8. **English text in the Vietnamese set (point 10).** The game menu title reads "Menu". Tab, I, T, N and J are key hints, which is acceptable. Also "Bản đồ thế giới" wraps to two lines in its tile. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/30-menu-phone-crafting-friends-skills-in-th.png`.
9. **Quantity-stepper frames do not show the stepper working.** 14, 17 and 20 show x1 still active and look the same as 13, 16 and 19. The audit's "x5 yes / All yes" is not visible. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/14-shop-seed-shop-quantity-stepper.png`.
10. **Misleading harvest message.** Harvesting an unripe crop with an empty hand shows "Có vật cản ở chỗ này." (there is an obstacle here). The quest stays at 0/1 with no explanation that the crop is not ready. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/10-farm-harvest-E-empty-hand.png`, `11-farm-wither-no-water-10-days.png`.
11. **Profile rename control not visible.** The audit says a rename control exists. The panel shows only the current name and the Google/Microsoft text. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/37-panel-profile-rename-N.png`.
12. **Stacked and stale toasts.** The level-up event shows four messages at once: banner, floating label, toast and a small toast touching hotbar slot 9. In combat, a stale "Cá đã thoát mất!" toast stays above the new one. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/40-level-up-banner-QA.png`, `25-combat-Z-with-no-monster-in-reach-toast.png`.
13. **Floating combat text overlaps the player name plate.** "-8", "CHÍ MẠNG -34" and "THĂNG CẤP!" sit on "Khách 2347". Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/27-combat-Z-with-a-monster-in-reach-cooldow.png`, `28-combat-X-fireball-monster-in-reach.png`, `40-level-up-banner-QA.png`.
14. **Combat frames cannot confirm their claims.** No monster is visibly beside the farmer in the teleport frame. No fireball is identifiable in the X frame. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/26-combat-teleport-next-to-a-monster-woods.png`, `28-combat-X-fireball-monster-in-reach.png`.
15. **Minimap not visible.** The audit says a phone map button exists. No minimap or map button appears in 03 or elsewhere. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/03-minimap-toggle-M.png`.
16. **Minor layout.** The quest card's left edge (x~10) does not match the time pill (x~48). A placed sprinkler dome peeks out behind the hotbar top edge. Dialog text wraps with orphan words ("nhé.", "ấm đã."). Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/390x844/01-HUD-start.png`, `08-farm-sprinkler-place-E.png`, `15-merchant-clothing-shop-dialog.png`, `39-NPC-dialogue-board-keeper.png`.

## Coverage gaps

The audit lists 10 features as "not exercised" with no screenshots: animals, pets, station crafting, market list and buy, mail send, co-op deposit, backpack upgrade, rename scroll, shipping bin, dungeon/mine combat. These were not graded.
