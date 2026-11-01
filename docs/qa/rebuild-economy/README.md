# Rebuild economy: market and shop captures

Before and after captures of the market (Sell, Browse, the price line and the listing result) in Vietnamese and English at **1280×720** and **390×844**.
Captured with `tests/browser/rebuild-economy/capture-market.js` (Playwright, Chromium): two fresh guests, a seller lists three stacks at the trader's
counter in Town, a buyer browses, sorts and opens the sell form.

- `before/`: `origin/master` (384cb1d), the build before this change.
- `after/`: this branch, with the sort row, the unit prices, the bin hint and the listing result.
- `<lang>-report.json` in each folder: what was captured, what was skipped and why, and any page error (none in either set).

| File | Screen |
|:--|:--|
| `seller-sell-empty` | Sell tab, nothing picked |
| `seller-sell-picked` | Parsnip picked, quantity and price typed (the fee line) |
| `seller-listed` | After listing: the result line |
| `buyer-browse` | Browse, newest first |
| `buyer-browse-sorted` | Browse, cheapest each (after only: the sort row is new) |
| `buyer-sell-picked` | Buyer picked a stack, no price yet (after only: the bin hint shows here) |
| `buyer-sell-priced` | Price typed: the fee line replaces the bin hint |

## Grading against `docs/design/ui-standard.md`, section 7

Grades: **P** pass, **F** fail, **N/A** not applicable. Where a check fails it was already failing on `master`; the change does not make it worse.

| # | Check | 1280×720 | 390×844 | Note |
|--:|:--|:--|:--|:--|
| 1 | Every element sits in its region | P | P | The market is a centre modal; the sort row and the bin hint are inside it. |
| 2 | Nothing overlaps | P | P | The bin hint is two lines at 1280 in Vietnamese; it ends inside the panel border. |
| 3 | Menu column | N/A | N/A | Not touched. |
| 4 | Edges line up | P | P | The sort row, the search row and the listing rows share the left edge. |
| 5 | `GamePanel`, one of the four widths | P | P | Unchanged (the market panel). |
| 6 | Three font sizes, no word-by-word wrap, nothing clipped | P | P | The bin hint and the listing result wrap as whole phrases at 390 (not word by word). |
| 7 | No inner scrollbar at 1280×720 for normal content | **F** | P | A browse page holds ten listings; at 720 px the panel shows six and scrolls the rest. Already so on `master`. Left for the owner (a shorter row or a smaller page). |
| 8 | Item icons whole and centred | P | P | Unchanged. |
| 9 | Spacing uses `--u` multiples and gap tokens only | P | P | Only existing controls (`TabBar`, `GameRow`, `GameInput`, `PriceInput`); no new CSS. |
| 10 | Vietnamese and English both fit | P | P | The sort tabs were too long in Vietnamese (`Giá mỗi món thấp nhất` wrapped the row at 390); shortened to `Rẻ nhất/món` and `Đắt nhất/món`, which fit one row. |

Section 8 (buttons): the sort row is a `tab` button (one value out of several, one active); the same kind as the Browse, Sell and My listings tabs.
Section 6 (touch, 390×844): the sort row fits on one line; touch targets are unchanged (14 `--u`).

## What changed on screen

- **Browse:** a sort row under the search (`Newest`, `Cheapest each`, `Priciest each`), and each listing of more than one unit shows its price per unit (`75 coins (25 each)`).
- **Sell:** once a stack is picked and before a price is typed, one line says what the shipping bin pays (`The shipping bin pays 40 coins each, 240 for this stack.`).
  Once a price is typed, that same line becomes the fee line, as before. One line only, so the form never grows.
- **Listing result:** `Listed 5 × Parsnip for 300 coins. You receive 285 coins when it sells, after the 5% fee.` (before: `Listed for sale.`).
