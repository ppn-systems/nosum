# QA captures: farming round four (ADR-104)

Branch `claude/farming-round4` from master `b60835ed`. Script `tests/browser/farm-round4-capture.cjs` (Development server, client published with
`-p:NosumDevTools=true`, `?qa=1`, saved language vi or en). Drag check `tests/browser/farm-round4-drag-probe.cjs` (frames in `drag/`).
Each file is one frame at the moment the message line shows the expected text, or after the step's wait: `<lang>-NN-<step>-<W>x<H>.png`.

- **before** = master `b60835ed`, built from a worktree of `origin/master` (`before/`). The 390x844 set is the width-override copy
  (`Device.MinimumWidth` 320 in that copy's `appsettings.json` only; the product keeps 1024).
- **after** = this branch (`after/`), same script and copy.
- The server is the same for both (this round has no server change; protocol 32).

The steps, in order: `farm-start`, `sown-row` (hoe and seeds by tap on A and B), `sprinkler-placed` (E facing the tile west of A, sprinkler in
hand), `sprinkler-watered-a` (day 1, a can tap on A), `scythe-tap-unripe` (day 2, the scythe TAP on A), `bin-sold` (a parsnip in hand, E south of the
shipping bin), `bin-after`.

Weather and the watering days are rolled per run (rain counts as water outdoors), so vi and en runs differ: the unripe line says "2 days left" in
en and "3 days left" in vi. Compare the lines within a run and the lines against the rule, not the number across runs.

## Grades

Grade: **pass** = the line is present, in the right language, the frame is clean; **partial** = present with a problem noted; **fail** = absent or wrong.

| Capture | Before (master) | After | Note |
|:--|:--|:--|:--|
| Scythe TAP on an unripe crop, 1280x720, en | **fail, silent** (`before/en-05`, no line; the scythe is selected, the farmer faces the row) | pass, "Not ready, 2 days left" (`after/en-05`) | ADR-104 D1: the tap never sent a request (`IsFarmTool`). |
| Scythe TAP on an unripe crop, 1280x720, vi | **fail, silent** (`before/vi-05`) | pass, "Chưa chín, còn 3 ngày" (`after/vi-05`) | |
| Scythe TAP on an unripe crop, 390x844, en | **fail, silent** (`before/en-05`) | pass, "Not ready, 2 days left" | Width-override copy. |
| Scythe TAP on an unripe crop, 390x844, vi | **fail, silent** (`before/vi-05`) | pass, "Chưa chín, còn 3 ngày" | The first run of this set missed the line once (a tap on the phone landed before the swing ended); the one-retry step in the script was added and the rerun hit at 59 ms. |
| Sprinkler placed, a line | **fail, silent** (`before/en-03`) | pass, "Placed." / "Đã đặt xuống." 1280x720 and 390x844 (`after/en-03`, `after/vi-03`) | ADR-104 D3. |
| Sprinkler waters the crop beside it (can tap on A, day 1) | pass, "The soil is already wet today." (`before/en-04`) | pass, same line | Worked on master; the check is that the sprinkler still waters. |
| Hover growth stage counts the sprinkler | not comparable across runs (weather) | pass by the rule: the after frames show "stage 2 of 5" with "3 days left" (parsnip: four watered days, five stages; one watered day counted, the sprinkler's day 1) | ADR-104 D2. The hover was a stage behind the server when the sprinkler was ignored (round three note). |
| Shipping bin sells the stack, E facing the bin | pass, "Sold for 80 coins." / "Đã bán được 80 xu." (`before/en-06`, `before/vi-06`) | pass, same lines, both sizes | End to end: two parsnips in hand, E at the bin, the parsnip leaves the hotbar. No change needed. |
| Drag-to-sow over a line of three | not on master's capture set | pass, "Seeds planted. ×3" and "Soil tilled. ×3" (`drag/`), seeds 18 to 15 | Round-three finding 5. The check passes; whether the first-run hint teaches the drag is an owner decision (copy). |
| Frame loop, page errors | none (no `pageerror` in any run) | none | |
| Layout (phone, 390x844 copy) | unchanged | unchanged | The message line now holds a placed line above the hotbar; no overlap in the frames. |

Not captured: a scythe tap on a ripe crop (needs about four watered days beyond these frames); the scythe drag (the one-tile-per-press rule is in code,
not in a frame).

## Files

- `before/` and `after/`: 14 frames per set (vi and en, seven steps), at 1280x720 and 390x844; 28 files each.
- `drag/`: the till and sow drags (`drag-till.png`, `drag-sow.png`).
