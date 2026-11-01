# QA captures: farming round four (ADR-105)

Branch `claude/farming-round4` (from master `b60835ed`, merged with master `d4fba266` before the PR). Script
`tests/browser/farm-round4-capture.cjs` (Development server, client published with `-p:NosumDevTools=true`, `?qa=1`, saved language vi or en).
Drag check `tests/browser/farm-round4-drag-probe.cjs` (frames in `drag/`). Each file is one frame at the moment the message line shows the expected
text, or after the step's wait: `<lang>-NN-<step>-<W>x<H>.png`.

- **before** = master `d4fba266`, built from a worktree of `origin/master` (`before/`). The 390x844 set is the width-override copy
  (`Device.MinimumWidth` 320 in that copy's `appsettings.json` only; the product keeps 1024).
- **after** = this branch (`after/`), same script and copy.
- The server is the same for both, the merged server of this branch (no server change in this round; protocol 32).
- An earlier pass on master `b60835ed` (before master moved) gave the same results; it is superseded by this set and not kept.

The steps, in order: `farm-start`, `sown-row` (hoe and seeds by tap on A and B), `sprinkler-placed` (E facing the tile west of A, sprinkler in hand),
`sprinkler-watered-a` (day 1, a can tap on A), `scythe-tap-unripe` (day 2, the scythe TAP on A; the before set adds a `-retry` step, one more tap,
when the line does not show), `bin-sold` (a parsnip in hand, E south of the shipping bin), `bin-after`.

Weather and the watering days are rolled per run (rain counts as water outdoors), so the vi and en runs differ: the unripe line says "2 days left" in
en and "3 days left" in vi. Compare the lines within a run and the lines against the rule, not the number across runs.

## Grades

Grade: **pass** = the line is present, in the right language, the frame is clean; **partial** = present with a problem noted; **fail** = absent or wrong.

| Capture | Before (master) | After | Note |
|:--|:--|:--|:--|
| Scythe TAP on an unripe crop, 1280x720, en | **fail, silent** (`before/en-05`, no line; the scythe is selected, the farmer faces the row; the retry `before/en-06-…-retry` is silent too) | pass, "Not ready, 2 days left" (`after/en-05`) | ADR-105 D1: the tap never sent a request (`IsFarmTool`). |
| Scythe TAP on an unripe crop, 1280x720, vi | **fail, silent** (`before/vi-05`) | pass, "Chưa chín, còn 3 ngày" (`after/vi-05`) | |
| Scythe TAP on an unripe crop, 390x844, en | **fail, silent** (`before/en-05`) | pass, "Not ready, 2 days left" (`after/en-05`) | Width-override copy. A first merged pass missed the line: the tap landed on a bare tile ("That does nothing here."), a phone-geometry flake; the set was re-taken in full. |
| Scythe TAP on an unripe crop, 390x844, vi | **fail, silent** (`before/vi-05`) | pass, "Chưa chín, còn 3 ngày" (`after/vi-05`) | |
| Sprinkler placed, a line | **fail, silent** (`before/en-03`) | pass, "Placed." / "Đã đặt xuống." 1280x720 and 390x844 (`after/en-03`, `after/vi-03`) | ADR-105 D3. |
| Sprinkler waters the crop beside it (can tap on A, day 1) | pass, "The soil is already wet today." (`before/en-04`) | pass, same line | Worked on master; the check is that the sprinkler still waters. |
| Hover growth stage counts the sprinkler | not comparable across runs (weather) | pass by the rule: `after/en-05` shows "stage 3 of 5" with "2 days left" (parsnip: four watered days, five stages; two watered days counted, so three of five and two to go) | ADR-105 D2. The hover ignored a sprinkler before, a stage behind the server (round-three note). |
| Shipping bin sells the stack, E facing the bin | pass, "Sold for 80 coins." / "Đã bán được 80 xu." (`before/en-07-bin-sold`, `before/vi-07-bin-sold`) | pass, same lines, both sizes (`after/…-06-bin-sold`) | End to end: two parsnips in hand, E at the bin, the parsnip leaves the hotbar. No change needed. |
| Drag-to-sow over a line of three | not captured on master | pass, "Seeds planted. ×3" and "Soil tilled. ×3" (`drag/`), seeds 18 to 15 | Round-three finding 5. The check passes. Whether the first-run hint teaches the drag is an owner decision (copy). The probe first hit a Summer refusal ("That crop does not grow in this season. ×3"); it now resets to spring first. |
| Frame loop, page errors | none (no `pageerror` in any run) | none | |
| Layout, phone (390x844 copy) | unchanged | the "Đã đặt xuống." line sits above the hotbar, clear of the stick and the touch buttons (`after/vi-03-…-390x844.png`) | |

Not captured: a scythe tap on a ripe crop (needs about four watered days beyond these frames); a scythe drag (the one-tile-per-press rule is in code,
not in a frame).

## Files

- `before/`: 28 frames of the seven steps (vi and en, 1280x720 and 390x844), plus the four `-retry` frames of the scythe tap (silent).
- `after/`: 28 frames of the seven steps (vi and en, 1280x720 and 390x844).
- `drag/`: the till and sow drags (`drag-till.png`, `drag-sow.png`).
