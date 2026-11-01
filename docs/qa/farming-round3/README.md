# QA captures: farming round three (ADR-100)

Branch `claude/farming-round3` from master `7800a2e`. Script `tests/browser/farm-round3-capture.cjs` (Development server, client published with
`-p:NosumDevTools=true`, `?qa=1`, saved language vi or en). Each file is one frame at the moment the message line shows the expected text, or after
the step's wait. `<lang>-NN-<step>-<size>.png`, size `1280x720` or `390x844`.

- **before** = master 7800a2e (`before/`). The 1280x720 set was taken with the same script on the master build; the 390x844 set was taken with the
  width-override copy (`Device.MinimumWidth` 320 in the copy's `appsettings.json` only, the product stays at 1024).
- **after** = this branch (`after/`), same script and copy.

The steps, in order: `farm-start`, `wide-hoe-tilled-row` (copper hoe, R), `sown-row`, `wet-soil-refusal` (second watering of a day),
`a-b-after-four-days`, `unripe-refusal` (scythe, E on C; C is sown on day 3 and watered twice, so it is unripe with two days to go),
`harvest-with-item-in-hand` (seeds in hand, E on A, ripe), `harvest-by-tap-with-can` (can in hand, a tap on B, ripe).

## Grades

Grade: **pass** = the line is present, in the right language and the frame is clean; **partial** = present with a problem noted; **fail** = absent or wrong.

| Capture | Before | After | Note |
|:--|:--|:--|:--|
| vi unripe line "Chưa chín, còn N ngày" 1280x720 | pass (`vi-06`) | pass | The round-two gap was the script's fixed wait; the poll takes the first frame that shows it (about 300 ms). |
| en unripe line "Not ready, N days left" 1280x720 | pass | pass | |
| vi unripe line 390x844 | pass | pass | Width-override copy; the line sits above the hotbar. |
| en unripe line 390x844 | pass | pass | |
| harvest with item in hand (E) 1280x720, vi | pass, old text "Đã thu hoạch bằng phím E…" (`vi-07`) | pass, "Đã thu hoạch, dù đang cầm đồ khác." | The text no longer names the key. |
| harvest with item in hand (E) 1280x720, en | pass, "Harvested with E…" | pass, "Harvested, even with another item in hand." | |
| harvest with item in hand (E) 390x844, en | pass, "Harvested with E, even with another item in hand." (`before/en-07`) | pass, "Harvested, even with another item in hand." | |
| harvest by tap with a can 1280x720 | **fail, silent** (`before/en-08`, the quest moved to "Sell your produce", no line) | pass | ADR-100 D2. |
| harvest by tap with a can 390x844, en | **fail, silent** (`before/en-08`, the quest moved to "Sell your produce", no line) | pass | |
| success lines (till, sow, water) 1280x720 | fail, silent (`before/en-02`, `en-03`, `en-04`) | pass ("Soil tilled.", "Seeds planted. ×2", "Watered. ×2") | ADR-100 D3. |
| success lines 390x844 | fail, silent | pass | |
| Frame loop at a fractional pointer position, 390x844 | **fail in the run where it occurred**: `Assert failed: Value is not an integer: 466.138671875`; the loop stopped. Two later before runs had no error. | pass: no pageerror in any after run | ADR-100 D1. The fault depends on where the pointer lands; it is not reproduced on every run. |
| Layout, 390x844, the touch layout | pass with notes: the stick, Chat, Run and Use buttons and the hotbar are drawn; the quest card covers about a fifth of the height; the hotbar has nine slots and no Z X V; slot counts are small | pass with the same notes | Owner decision (critique round three, finding 8). |
| Hint over the work area, 1280x720 | partial (`before/vi-07`, the hint covers the row, y 460 to 525) | partial, unchanged | Critique round three, finding 3. |
| Scythe tap on an unripe crop | fail, silent (no line in three runs) | fail, silent (unchanged) | Critique round three, finding 1; cause not found. |

Not captured this round: the sprinkler, the shipping bin and the coin pop (see the addendum). The `-x4` tile crops of round two were not repeated.

## Files

- `before/` and `after/`: 16 frames each per size (vi and en; 8 steps), at 1280x720 and 390x844.
