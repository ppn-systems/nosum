# QA grade: world-pass5, vi, 1280x720

Scope: 39 PNG frames in `/home/user/nosum/docs/qa/world-pass5/vi/1280x720/` (not ~50; the audit lists 39 screenshots plus 11 "not exercised" rows with no frame). Every frame was viewed. Audit JSON read from `/home/user/nosum/docs/qa/world-pass5/audit-vi-1280x720.json`.

Point key: 1 HUD region, 2 overlap, 3 menu column (one column, max 8 same-size), 4 edge alignment, 5 panel frame/title/close, 6 text fits, 7 no inner scrollbar, 8 icons whole, 9 spacing, 10 language.

Scrollbars (7) and clipped text (6) were not seen in any frame. Panels all have title left and close right (5 ok).

| frame | status (audit) | points failing | grade | note |
|:--|:--|:--|:--|:--|
| 01-HUD-start | captured | none | PASS-WITH-NOTES | Quest title text is low contrast on grey bar (same in all quest frames). |
| 02-running-energy | captured | none | PASS | Same HUD as 01; energy drain not visible in a still frame. |
| 03-minimap-toggle-M | pass | 1 | FAIL | Minimap still drawn after M; audit says height 132 -> -1, screenshot contradicts it. Identical to 02. |
| 04-farm-grant-seeds-sprinkler-rod | captured | none | PASS | Toast, hotbar and grant items clean. |
| 05-farm-till-hoe-E | pass | none | PASS | Quest moved to water; toast clean. |
| 06-farm-sow-seeds-E | pass | none | PASS | Two stacked toasts centred, no overlap. |
| 07-farm-water-can-E | pass | none | PASS | Quest moved to harvest; counts match audit. |
| 08-farm-sprinkler-place-E | captured | none | PASS-WITH-NOTES | Sprinkler appears on the planted tile (dark block); crop not legible. See Issue 3. |
| 09-farm-advance-4-days-grow | captured | 1 | FAIL | Two-line quest reminder card floats mid-screen over the world (not in any HUD region). No crop visible after 4 days. |
| 10-farm-harvest-E-empty-hand | captured | 1 | FAIL | Same mid-screen card persists. "Có vật cản ở chỗ này" (obstacle here) contradicts a harvest attempt. Bag unchanged. |
| 11-farm-wither-no-water-10-days | captured | 1 | FAIL | Card still floating; no withered crop visible; the tile still shows only the block. |
| 12-merchant-seed-shop-dialog | pass | 3 | FAIL | Dialog clean. Top-right column reduced to one orphan button (see Issue 1). |
| 13-shop-seed-shop-buy-list | pass | 3 | FAIL | Shop panel layout clean (title left, X right, 2-col list, pager). Orphan button. |
| 14-shop-seed-shop-quantity-stepper | captured | 3 | FAIL | Identical to 13: x1 still selected, qty 1, total 12 xu. x5 / Tất cả not shown. |
| 15-merchant-clothing-shop-dialog | pass | 3, 9 | FAIL | Option row wraps: "Tạm biệt" alone on a second row (ragged). Orphan button. |
| 16-shop-clothing-shop-buy-list | pass | 3 | FAIL | Clean otherwise. Orphan button. |
| 17-shop-clothing-shop-quantity-stepper | captured | 3 | FAIL | Identical to 16; stepper state not shown. |
| 18-merchant-furniture-shop-dialog | pass | 3 | FAIL | Dialog clean. Orphan button. |
| 19-shop-furniture-shop-buy-list | pass | 3 | FAIL | Clean otherwise. Orphan button. |
| 20-shop-furniture-shop-quantity-stepper | captured | 3 | FAIL | Identical to 19; stepper state not shown. |
| 21-market-open | captured | 3 | FAIL | Market tabs, search, sort, empty state all clean. Orphan button. |
| 22-fishing-cast-at-the-shore | pass | none | PASS | Waiting toast; full menu column. |
| 23-fishing-bite-QA | captured | none | PASS-WITH-NOTES | Bite prompt reads well; "!" alert box butts into the name plate. |
| 24-fishing-reel-in-and-catch-card | captured | none | PASS | "Cá đã thoát mất!" (escaped), no catch card, as the audit says. |
| 25-combat-Z-no-monster-toast | pass | none | PASS-WITH-NOTES | Old fishing toast still stacked above the new one (stale message). |
| 26-combat-teleport-next-to-monster | captured | none | PASS-WITH-NOTES | World name labels sit at the screen edges (TRỤ LÔI ĐIỆN top-right; CUỒNG HỎA at bottom edge). |
| 27-combat-Z-monster-in-reach | pass | none | PASS-WITH-NOTES | Damage numbers stack directly over the player name plate. |
| 28-combat-X-fireball | captured | none | PASS-WITH-NOTES | Combo buff card sits above hotbar right edge (aligned to hotbar). Fireball not visible in still. |
| 29-combat-V-heal-toast | pass | none | PASS-WITH-NOTES | Toast is "Không đủ MP" (not enough MP), not a heal result. "!" marker sits on the name plate. |
| 30-crafting-panel | captured | 3, 9 | FAIL | Recipe grid indented from the section margin (ragged). Orphan button. |
| 31-panel-mail | captured | 3 | FAIL | Panel is much smaller than the others (sizes vary). Orphan button. |
| 32-panel-friends | captured | 3, 10 | FAIL | Help text reads "Gỗ /report" (wood) where "Gõ" (type) is meant; "/report" is English. Verify at full zoom. Orphan button. |
| 33-panel-skills | captured | 3 | FAIL | Bars and skill rows clean. Orphan button. |
| 34-panel-crafting | captured | 3, 9 | FAIL | Same as 30. |
| 35-panel-co-op-warehouse-G | captured | 3 | FAIL | Shows the create-co-op form, not the warehouse list the audit expects. Orphan button. |
| 36-panel-profile-rename-N | captured | 3 | FAIL | Text fits in the quote box. No rename control visible (consistent with quest not done). Orphan button. |
| 37-calendar-season-passes-QA | pass | 1, 2, 4 | FAIL | "Chế tạo" tag sits over the top column button and sticks out past the column/minimap right edge. |
| 38-NPC-dialogue-board-keeper | pass | none | PASS | Full 8-button column; dialog clean. |
| 39-level-up-banner-QA | pass | 2, 4 | FAIL | Ability tooltip "Cầu lửa" covers the "Thắng cấp" toast. Level-up banner left edge does not align with the calendar bar. |

Totals: PASS 8, PASS-WITH-NOTES 8, FAIL 23.

## Issues (most severe first)

1. **Menu column collapses to one orphan button whenever a panel or dialog is open.** Point 3 and 1. The minimap is hidden and only one button shows at top-right (about y 105), the other seven vanish. Full column is correct in 01-11, 22-29, 37-39. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/1280x720/12-merchant-seed-shop-dialog.png`, `13-shop-seed-shop-buy-list.png`, `21-market-open.png`, `30-crafting-panel.png`, `31-panel-mail.png`, `36-panel-profile-rename-N.png`. If the design intends a collapsed column while a modal is open, the frames should show that deliberately; as drawn it reads as broken. This accounts for 20 of the 23 FAILs.

2. **Quest reminder card floats over the world mid-screen and persists.** Point 1. A two-line card ("Gieo hạt giống lên đất đã xới...") sits at about y 418-485 over the play area, outside every HUD region, and stays across three frames. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/1280x720/09-farm-advance-4-days-grow.png`, `10-farm-harvest-E-empty-hand.png`, `11-farm-wither-no-water-10-days.png`.

3. **Farm state contradicts itself: no visible crop, sprinkler on the planted tile, harvest blocked.** Functional. After sowing, watering and placing a sprinkler on that tile (08), no crop shows after four game days (09), the harvest attempt says "Có vật cản ở chỗ này" (10), and the withered test shows no crop either (11). The dark block at the tile is likely the sprinkler. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/1280x720/08-farm-sprinkler-place-E.png`, `10-farm-harvest-E-empty-hand.png`.

4. **Minimap still visible after pressing M.** Functional, and contradicts the audit's "pass". Frame 03 is pixel-identical to 02. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/1280x720/03-minimap-toggle-M.png`.

5. **Ability tooltip covers a toast, and the level-up banner is misaligned.** Points 2 and 4. "Cầu lửa / Phóng cầu lửa tầm xa (25 MP)" sits over the "Thắng cấp..." toast, hiding most of it. The banner's left edge (about x 710) does not match the calendar bar (about x 730). Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/1280x720/39-level-up-banner-QA.png`.

6. **"Chế tạo" tag overlaps the top menu button and overhangs the right edge.** Points 2 and 4. The tag at about x 1172-1270, y 225-270 covers the first column button and extends past the minimap and column right edge (about x 1250-1256). Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/1280x720/37-calendar-season-passes-QA.png`.

7. **Quantity-stepper frames are identical to their predecessors.** Evidence gap or functional issue. Frames 14, 17 and 20 match 13, 16 and 19 exactly: x1 still selected, qty 1, total 12 xu. The audit's "x5 yes; All yes" is not visible. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/1280x720/14-shop-seed-shop-quantity-stepper.png`, `17-shop-clothing-shop-quantity-stepper.png`, `20-shop-furniture-shop-quantity-stepper.png`.

8. **Possible wrong word in the Friends help text.** Point 10. "Gỗ /report tên trong khung chat" reads as "wood", where "Gõ" (type) is meant. "/report" is English. Confirm at full resolution. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/1280x720/32-panel-friends.png`.

9. **Clothing dialog option buttons wrap unevenly.** Point 9. "Tạm biệt" sits alone on a second row, with "Mua/Thay đồ/Mở rộng túi/Trò chuyện" on the first. Evidence: `/home/user/nosum/docs/qa/world-pass5/vi/1280x720/15-merchant-clothing-shop-dialog.png`.

10. **Audit notes do not match the frame content.**
    - 35 is labelled "co-op warehouse (G)" but shows only the create-co-op form.
    - 29 is labelled "heal at full health" but the outcome is "Không đủ MP".
    - 36 is labelled "rename control" but none is visible. This is consistent with the first quest not being complete, so it is a labelling issue, not a defect.
    Evidence: `35-panel-co-op-warehouse-G.png`, `29-combat-V-heal-at-full-health-toast.png`, `36-panel-profile-rename-N.png` (all under `/home/user/nosum/docs/qa/world-pass5/vi/1280x720/`).

11. **Minor spacing and overlap notes.**
    - "!" alert box touches the player name plate (23, 29).
    - Damage numbers stack over the name plate (27).
    - Stale fishing toast stacked above the new one (25).
    - Recipe grid indented from the section text margin (30, 34).
    - Panel sizes vary: mail (31) and friends (32) are much smaller than shop and skills (13, 33).
    - Quest title text is low contrast on the grey title bar (01 and all quest frames).
    - Edge labels crowd the screen corners (26).
