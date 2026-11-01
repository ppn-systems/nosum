# rebuild-farming: QA captures

Captures of the farming loop on `master` (before) and on `claude/rebuild-farming` (after), made with
`tests/browser/farm-loop-play.cjs` and `tests/browser/farm-device-block-capture.cjs` against a Development server and a tester client
(`-p:NosumDevTools=true`). Critique and backlog: `docs/design/critique-farming.md`. Decision: `docs/adr/ADR-080-wide-use-count-and-the-wet-soil-refusal.md`.

## What the runs show, and what they do not

- **The farmer stays on one tile** for the whole run (one step south, then no walking): till, sow, water, water again the same day, water on each of
  four game days (`DevTools.AdvanceClock(1)`), harvest with the scythe, then a wide swing with a copper hoe over the bare line.
- **Weather is rolled per run.** On one run it rained on days 1 to 3 and the can was refused; on another it did not. Compare toasts across runs with that in mind.
  The day-4 "water" press on a ripe crop harvests it (the action key works a ripe crop whatever is held), so the scythe step can find nothing to harvest.
- **Language is a client setting**, not the browser locale. The "after" and the corrected "before" English runs set `lang=2` in `nosum.settings.v1`.
  An earlier English "before" run used only the locale and came out Vietnamese; it was replaced. The first exploratory run is kept in
  `critique-run-1/` for reference (its frame names overlap the later set).
- **390×844 is not a playable layout.** The device guard blocks touch devices and any window narrower than 1024 px (`Device_NarrowText`), so the
  captures at that size are the block screen, in both languages, before and after.
- **1920×1080 was not captured** in this pass.
- The energy label that floats on a wide use is not visible in these frames (it lasts about a second and the crops are taken after it has gone).
  The energy change is covered by `FarmWireTests` (the count) and `FarmChannelTests` (the count reaches the subscribers), not by a frame.

## Toast text, before and after (1280×720)

| Moment | vi before | vi after | en before | en after |
|:--|:--|:--|:--|:--|
| Watering wet soil the same day | "Dùng vật phẩm này ở đây không có tác dụng." | "Đất đã đủ nước hôm nay." | "That does nothing here." | "The soil is already wet today." |
| Watering on a rainy day | "Dùng vật phẩm này ở đây không có tác dụng." on days 1 to 3 | no rain in this run, so no refusal | "That does nothing here." on day 0 (rain) and on the same day | "The soil is already wet today." on days 1 to 3 (rain) |
| Tilling a tilled tile | "Đất đã được xới rồi." | (unchanged) | (unchanged) | "The soil is already tilled." |

## Grades against `docs/design/ui-standard.md` section 7 (1280×720, English frame `after/1280x720-en/09-grown-full.png`)

The change here touches only a toast's text and a floating number, so the layout grades are the same before and after.

| # | Item | Grade | Note |
|:--|:--|:--|:--|
| 1 | Every element in its region | Pass | Quest card top-left, toast above the hotbar, hotbar bottom. |
| 2 | Nothing overlaps | Partial | The toast covers the tile two below the farmer. It does not cover the hotbar or another toast. |
| 3 | Menu column: one column, at most 8 buttons | **Fail** | The top-right has two columns of icon buttons, about 14. |
| 4 | Edges line up | Partial | The clock and money bars and the minimap end at x 1157; the icon grid runs to x 1268. |
| 5 | Panels use `GamePanel` widths | N/A | No panel open in the loop. |
| 6 | Three font sizes, no broken wrapping, nothing clipped | Partial | Wrapping is clean. A label is clipped at the right edge (x about 1270, y 255). |
| 7 | No inner scrollbar | N/A | No panel. |
| 8 | Item icons whole and centred | Pass | Hotbar icons, counts in the corner. |
| 9 | Spacing uses `--u` and gap tokens | Not verified | Not measurable from a screenshot. |
| 10 | Vietnamese and English both fit | Pass for en | "Sell produce at the / shipping bin" wraps on a word; the toast is two centred lines. vi frames fit too. |

Not graded: 390×844 (the device block, see above) and 1920×1080 (not captured).

## Files

- `before/1280x720-vi`, `before/1280x720-en`: master, play run plus frames and `playlog.txt`.
- `before/390x844-vi/device-block.png`, `before/390x844-en/device-block.png`: master, block screen.
- `after/1280x720-vi`, `after/1280x720-en`: this branch, same script.
- `after/390x844-vi/device-block.png`, `after/390x844-en/device-block.png`: this branch, block screen (unchanged, as expected).
- `critique-run-1/1280x720-vi-exploratory`: the first exploratory run, kept for reference.
