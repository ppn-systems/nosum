# Economy round 4: the browse page holds the rows the window fits

Round 4 of the market layout ([ADR-101](../../adr/ADR-101-market-listing-rows-and-expiry-on-my-listings.md), amendment 2026-11-01). Round 3 left one check
open: at 1280×720 and 1920×1080 a page of eight listings scrolled inside the panel (`ui-standard.md` section 7, item 7). The coordinator's decision: keep the
14 u row buttons, and let the page size follow the room the panel has. The page asks the server for the rows that fit, and the server clamps to 20.

## The measure

`.menu-page-body` is the panel's scroll box. `clientHeight` is the room; `scrollHeight` is what the page needs. The page has no inner scrollbar when they
are equal (`fits` in the reports). The capture (`tests/browser/rebuild-economy/capture-market.js`, `FIT=1`) lists sixteen stacks, one parsnip each, so
each window has more listings than rows, then reads the body once the browse page has settled.

## Captures

- `after/<WxH>/<lang>-buyer-browse.png`: the Browse page, 1280×720, 1920×1080 and 390×844, Vietnamese (`vi`) and English (`en`).
- `after/<WxH>/<lang>-report.json`: the report of that run (`measures`: client and scroll height, rows shown, row and list height, the pager text).
- `after/<WxH>/en-report-repeat{1,2,3}.json`: three more runs at 1280×720 in English, to show the result is stable.
- `after/<lang>-report.json` at the top: the last run of that language (390×844), kept by the script. The per-size reports above are the record.
- Before: the round-3 `after` set (`docs/qa/economy-round3/`), eight rows per page, is the before of this round: 31 px of inner scroll at 1280×720 and
  157 px at 1920×1080.

## Result

| Size | Language | Rows per page | Body: visible / needed | Inner scroll | Round 3 (8 rows) |
|:--|:--|--:|:--|:--|:--|
| 1280×720 | en | 7 | 499 / 499 | none | 516 / 547 (31 px) |
| 1280×720 | vi | 7 | 499 / 499 | none | |
| 1920×1080 | en | 5 | 668 / 668 | none | 751 / 908 (157 px) |
| 1920×1080 | vi | 5 | 668 / 668 | none | |
| 390×844 | en | 15 | 621 / 621 | none | 397 / 397 (fits) |
| 390×844 | vi | 15 | 621 / 621 | none | |

Three runs at 1280×720 in English gave the same numbers (`en-report-repeat1..3.json`).

The rows are 42 px at 720 (the 14 u button, `--u` 3), 70 px at 1080 (`--u` 5) and 28 px at 390 (`--u` 1). The count comes from the room, not from a fixed number.

## Grading against `docs/design/ui-standard.md`, section 7 (after)

Grades: **P** pass, **F** fail, **N/A** not applicable. Checked on the final `after` set.

| # | Check | 1280×720 | 1920×1080 | 390×844 | Note |
|--:|:--|:--|:--|:--|:--|
| 1 | Every element sits in its region | P | P | P | No element moved. The list is the Browse page's own stack. |
| 2 | Nothing overlaps | P | P | P | The page is the same stack; the list ends above the pager. |
| 3 | Menu column | N/A | N/A | N/A | Not touched. |
| 4 | Edges line up | P | P | P | Rows share the left edge with the filter row. |
| 5 | `GamePanel`, one of the four widths | P | P | P | Width and height rules unchanged. |
| 6 | Three font sizes, no word-by-word wrap, nothing clipped | P | P | P | Long names end in an ellipsis on one line (ADR-101 D2). |
| 7 | No inner scrollbar at 1280×720 and above | **P** | **P** | P | `clientHeight == scrollHeight` at every size, in both languages. Was F at 720 and 1080. |
| 8 | Item icons whole and centred | P | P | P | The compact picture (8 u), unchanged. |
| 9 | Spacing uses `--u` multiples and gap tokens only | P | P | P | No CSS added. |
| 10 | Vietnamese and English both fit | P | P | P | `Tìm`, `Mới nhất`, `Rẻ nhất`, `Đắt nhất`, `Trang 1/3` fit at 1280. |

Section 6 (touch, 390×844): the list fits without scrolling. The row buttons are the 14 u `GameButton`s, the same as at desktop.

## Bugs found while measuring

- **A late browse reply could put back an old page size.** Seen once, in the first cold run at 1280×720 (10 rows, 643 px needed, not fitted). The
  cause is inferred from the code, not reproduced on demand: the panel asks for a page at open and again on the first measure, and a reply to the older
  ask that arrives last would be shown. `MarketChannel` now shows only the newest browse (`MarketBrowseOrderTests`, two tests that answer out of order).
  The fit was right in every run after the fix (ten more captures, listed above).
- **Accumulated listings.** A first run without clearing the market counted earlier sizes' listings in the pager (`Page 1 of 7` for sixteen listings).
  The captures now clear `market_listings` and `market_sales` before each size.

## Tests

- Unit: `MarketSortPacketTests` (the field round-trips, the default is zero, protocol 34), `MarketSortViewModelTests` (`SetPageSize` asks for the rows
  and clamps them; the same size asks nothing; `RowsThatFit` counts whole rows and gaps; `PageCount` counts in the size asked),
  `MarketBrowseOrderTests` (out-of-order replies).
- Integration: `SocialServiceTests.Browse_PageSize_AsksForThatManyRowsAndClampsToTheLimit` (four rows asked, zero is ten, 500 is clamped to 20).
- Builds: every `.slnx` in the repo (`bench`, `contracts`, `engine`, `gameplay`, `server`, `tests`, `tools`, `wasm`), 0 warnings, 0 errors.
- `dotnet test tests/Nosum.Tests.slnx` on the merged tree (this branch with `origin/master` 1b2df17, QA round 7): unit **3721 passed, 0 failed**;
  integration **835 passed, 0 failed**.
- The captures were taken from the merged build (the host and the client published from it), in the same run as the grading above.

## Open

- **The price of current asks** (critique section 6, item 8) needs a server query for open asks; a wire change, for a later round.
- **The recurring late-game sink** (critique section 2) is still the owner's decision.
