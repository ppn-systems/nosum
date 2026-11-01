# Economy round 2: captures and grading

Before and after captures of the market in Vietnamese and English at **1280×720** and **390×844**, for the second economy pass
([`critique-economy.md`](../../design/critique-economy.md) sections 2, 4, 6 and 7; ADR-013 A10, ADR-081 round-2 amendment).

- `before/`: `master` (0c89230), the build before this change. Captured with `tests/browser/rebuild-economy/capture-market.js`.
- `after/`: this branch. The same script with `LARGE_BUY=1`, which adds the large purchase (`buyer-buy-confirm`, `buyer-bought-large`).
- `economy-report-after.txt`: the simulation (`EconomyReportTests`, `NOSUM_ECONOMY_REPORT`) on this branch's catalogs.

Caveat on comparability: the local dev database held listings from earlier, failed capture runs, so the `after` browse pages show more
listings (up to ten per page, `Trang 1/4` at the phone size) than the `before` set, which had three. The rows compared at 1280×720 are the same
kind of row (one stack, a total and a unit price).

| File | Screen |
|:--|:--|
| `seller-sell-empty` / `seller-sell-picked` / `seller-listed` | Sell tab and the listing result (unchanged data) |
| `buyer-browse` | Browse, newest first |
| `buyer-browse-sorted` | Browse, cheapest each |
| `buyer-sell-picked` / `buyer-sell-priced` | Sell form: bin hint, then the fee line |
| `buyer-buy-confirm` | **New.** A 1,500-coin listing, Buy pressed: the confirm box |
| `buyer-bought-large` | **New.** After Confirm: the list again |

## Grading against `docs/design/ui-standard.md`, section 7

Grades: **P** pass, **F** fail, **N/A** not applicable. Checked on the `after` set.

| # | Check | 1280×720 | 390×844 | Note |
|--:|:--|:--|:--|:--|
| 1 | Every element sits in its region | P | P | The confirm box is a centre overlay (`MarketBuyOverlay`, like the pet purchase box). |
| 2 | Nothing overlaps | P | P | The confirm box covers the list only while it is open. |
| 3 | Menu column | N/A | N/A | Not touched. |
| 4 | Edges line up | P | P | Listing rows share the left edge. |
| 5 | `GamePanel`, one of the four widths | P | P | The confirm box is a `ConfirmDialog` (a `GamePanel`, the S width). |
| 6 | Three font sizes, no word-by-word wrap, nothing clipped | P | P | Seller names and listing names are one line now, with an ellipsis (they used to wrap to two lines). |
| 7 | No inner scrollbar at 1280×720 for normal content | **F** | P | **Not met.** Six rows fill the panel at 720 px and the pager sits below the visible area, so a page needs scrolling. See "Left open". |
| 8 | Item icons whole and centred | P | P | Unchanged. |
| 9 | Spacing uses `--u` multiples and gap tokens only | P | P | One new CSS rule (`MarketPanel.razor.css`: ellipsis on the name and seller); no other CSS. |
| 10 | Vietnamese and English both fit | P | P | The confirm text is one line at 390 in Vietnamese (`Mua 9 Củ cải vàng với giá 1.500 xu?`). |

Section 6 (touch, 390×844): the confirm buttons are the same `GameButton` as the rest of the page; touch targets are unchanged.

## What changed on screen

- **Large purchase:** a purchase of 1,000 coins or more asks first (`Confirm purchase`, what, how many and the price, Confirm and Cancel).
  Smaller purchases go through at once, as before.
- **Listing rows:** a name or seller that is too long ends in an ellipsis on one line (it wrapped to two lines before).

## Left open (owner)

- **Six rows without a scroll (item 5 of the round-2 brief): not met.** At 1280×720 the market panel's list area is about 200 px. One listing
  row is an 18-unit item slot (54 px) and the fixed parts above the list (title, tabs, search, sort, pager) take about 245 px. Six full rows need about
  380 px. The fit needs one of: a smaller item picture in the listing rows (a change to the shared `ItemSlotButton`, which the UI standard keeps one size),
  the search and the sort on one row, or a shorter page. Those are layout decisions for the owner; nothing here changes the slot size.
- **A late-game sink that recurs** (critique section 2): not built. The retune adds one-time cost and feed; see ADR-081's round-2 amendment.
