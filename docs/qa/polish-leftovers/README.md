# QA captures: polish leftovers

Branch `claude/polish-leftovers`, from master `c5d737c`. Before = master, after = branch head with the six items.

- `before/` and `after/`: PNG per state, language and viewport. States: `target-thirsty` (target cursor on a thirsty tile, with a 4x zoom crop),
  `shop-seedkeeper` (the seed shop's `ShopPanel`, the panel with item rows), `market-sell` (Market, Sell tab), `withered` (the farm with a withered
  crop). Languages: `vi` and `en`. Viewports: 1280x720, 1920x1080, 390x844.
- `before/metrics/` and `after/metrics/`: layout numbers read from the page (window and body heights, `scrollHeight` against `clientHeight`,
  row heights, the grid's gaps to the body edges, computed `--u`).
- `withered-tile-A-before-after-x4.png`: the withered tile before and after, 4x.

Captures are from a QA build (`NosumDevTools=true`), driven through `window.nosumQa`. The radish does not grow in winter, so some runs were
retried in spring or summer.

## What changed, per item

| # | Item | Result | Evidence |
|:--|:--|:--|:--|
| 1 | Sprite publish guard | `PublishedSpritesTests` checks every `assets/` sprite the client requests (literals, bare names, animal and building catalogues) against the wasm csproj. It found that the duck, goat, pig and sheep sheets were not published, so buying one would 404. They are published now. | unit tests |
| 2 | Palette generator | `palette.py` now emits `--nosum-leaf`, `--nosum-sky` and their comment, as the checked-in CSS has them. The retired `start_background.png` generator and its provenance row are removed. `repack_cc0_art.py --sources art/source --check` exits 0 with no output. | generator check |
| 3 | Drop above the highlight | No code change. The drop is already queued after the reticle in the same command buffer, and the zoom shows the drop over the reticle's top-right bracket. | `target-thirsty-*-x4-zoom.png` |
| 4 | Shop rows | The list shows as many whole rows as fit (4 at 1280x720, 8 at 390x844 in the probe), with no inner scroll. At 1920x1080 it shows 3 rows, not the aimed-for 8 (see deviations). | `shop-seedkeeper-*`, metrics |
| 5 | Market Sell grid | The grid now has equal gaps to the panel body (0 and 0 in the measured page). The empty-state text had a 2 px overflow at 1920 that caused a scroll; fixed with one `u` of padding. | `market-sell-*`, metrics |
| 6 | Withered crops | A dedicated dry, brown plant (Nosum's own drawing in `tools/art_repack/crops.py`, frame 1 of row 0 of `crops.png`) replaces the tinted stalk. Provenance row updated. | `withered-*`, `withered-tile-A-before-after-x4.png` |

Seed shop metrics (vi), body `clientHeight` and `scrollHeight`:

| Size | Before | After |
|:--|:--|:--|
| 1280x720 | 4 rows, 499 / 499 | 4 rows, 499 / 499 |
| 1920x1080 | 4 rows, dialog 900 px | 3 rows, 717 / 717 |
| 390x844 | 4 rows | 8 rows, 500 / 500 |

Market Sell grid, gap to the body edges (left / right): before 0 / 3 px at 1280, 0 / 5 px at 1920, 0 / 2 px at 390. After 0 / 0 at all three.

## Self-check against the 10-point checklist (`docs/design/ui-standard.md` section 7)

This is the lead's own check from the captures and metrics, not an independent review.

1. Every element sits in its region: yes. No region or panel changed; the shop list and market grid stay in their `GamePanel`.
2. Nothing overlaps: yes for the captured states. The 1920 seed shop image and the 1280 metrics show no overlap with the HUD or the pager.
3. Menu column: unchanged.
4. Edges line up: yes. The market grid's gaps are equal on both sides.
5. Panels use `GamePanel` with the shared S/M/L/XL widths: unchanged.
6. Three font sizes; nothing clipped: yes. No new font sizes. The market empty-state text no longer runs past its box.
7. No inner scrollbar at 1280x720 and above: yes, in every captured shop and market state (metrics above). The 1920 shop shows 3 rows, not 8, to achieve this.
8. Item icons whole and centred: unchanged.
9. Spacing in `--u` multiples: yes. The market wrapper uses `margin-right: -1u` to fill the body's padding; the shop uses the measured row count only.
10. Vietnamese and English both fit: the captures exist in both. The 1920 shop image checked by eye is vi; en has identical metrics.

## Deviations from the brief

- **Item 3** needed no code change. The highlight was never drawn after the drop in the code; the capture confirms the drop sits on top.
- **Item 4**, the 1920x1080 shop shows 3 rows instead of 8. With the seed shop's detail strip, 4 rows overflow the window at 1920 (817 px of body in a 743 px box). The row floor is 3 so the window never scrolls. Making 8 rows fit there would need a shorter detail strip, a design change this polish pass did not make.
- **Item 4**, the trader's Buy list is the Market panel's Buy tab, not `ShopPanel`. The captures use the seed shop, which is the `ShopPanel` with the item rows.
- **Item 6**: one shared withered frame for every crop, not one per crop.
