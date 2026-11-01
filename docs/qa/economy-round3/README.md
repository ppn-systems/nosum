# Economy round 3: captures and grading

Before and after captures of the market Browse page in Vietnamese and English at **1280×720**, **1920×1080** and **390×844**
(round 3 of the market layout: [ADR-097](../../adr/ADR-097-market-listing-rows-and-expiry-on-my-listings.md),
[`critique-economy.md`](../../design/critique-economy.md) section 11).

- `before/`: `origin/master` (0d0b383) with **one** change, the `renderer.js` fix from this branch (commit 9a27c60). Without that fix master's
  client does not load (`Identifier 'fitLabel' has already been declared`), so there is no master capture without it. Nothing else differs, so
  `before/` shows the round-2 labels (`Cheapest each`, `Search by item name`).
- `after/`: this branch.
- Both sets are taken by `tests/browser/rebuild-economy/capture-market.js` on the same local server and the same data: a seller lists eight
  stacks (12 parsnips), a buyer browses. The database is cleared before each size, so every page holds exactly eight listings.
- `<lang>-<WxH>-report.json`: the capture report, with the scroll measure of the menu body (`measures`) for that size.
- Screens: `<WxH>/<lang>-buyer-browse.png` and `…-buyer-browse-sorted.png`, `…-seller-*.png`, `…-buyer-sell-*.png`.

## The measure

The check in `ui-standard.md` section 7, item 7 is "no inner scrollbar". The capture reads `.menu-page-body` (the scroll box of the
panel) once the browse page has eight rows:

| Size | Language | Before: body (visible / needed) | After: body (visible / needed) | After: result |
|:--|:--|:--|:--|:--|
| 1280×720 | en, vi | 525 / 697 (172 px scroll) | 516 / 547 (31 px scroll) | **Scrolls** |
| 1920×1080 | en, vi | 755 / 1158 (403 px scroll) | 751 / 908 (157 px scroll) | **Scrolls** |
| 390×844 | en, vi | 465 / 465 | 397 / 397 | Fits |

Why the desktop still scrolls: the row buttons (`Prices`, `Buy`) are the theme's 14 u button, so a row is 42 px at 720 and 70 px at 1080
(`--u` is 3 and 5). The compact item picture (12 u) does not change the row height; the button does. The dialog's height is capped by the shared
HUD band (`TradeDialog`, `--nosum-hud-top-h`), which this change does not touch. By the same numbers, about seven rows would fit at 720 and
five at 1080 (computed from the measure, not captured).

## Grading against `docs/design/ui-standard.md`, section 7 (after)

Grades: **P** pass, **F** fail, **N/A** not applicable. Checked on the final `after` set (1280×720, 1920×1080 and 390×844, vi and en).

| # | Check | 1280×720 | 1920×1080 | 390×844 | Note |
|--:|:--|:--|:--|:--|:--|
| 1 | Every element sits in its region | P | P | P | Search, sort and list are the Browse page's own stack. No new card. |
| 2 | Nothing overlaps | P | P | P | |
| 3 | Menu column | N/A | N/A | N/A | Not touched. |
| 4 | Edges line up | P | P | P | Listing rows share the left edge; the filter row and the list share the body's edges. |
| 5 | `GamePanel`, one of the four widths | P | P | P | Width unchanged. |
| 6 | Three font sizes, no word-by-word wrap, nothing clipped | P | P | P | Long seller names end in an ellipsis on one line (by design, ADR-097 D2). The search placeholder is shorter (`Search items`, `Tìm vật phẩm`), so it is not clipped at 1280 (it was: `Search by item r`). |
| 7 | No inner scrollbar at 1280×720 and above | **F** | **F** | P | 31 px at 720, 157 px at 1080. See "Open". |
| 8 | Item icons whole and centred | P | P | P | The compact picture (8 u) is the whole sprite, scaled to fit. |
| 9 | Spacing uses `--u` multiples and gap tokens only | P | P | P | |
| 10 | Vietnamese and English both fit | P | P | P | Checked the vi captures: the sort labels (`Rẻ nhất`, `Đắt nhất`) and the placeholder fit. |

Section 6 (touch, 390×844): the phone layout puts the search field on its own line, and the Search button and the sort tabs on the line
below. Touch targets are the same `GameButton`s as before.

## What changed on screen

- **Search and sort on one row** (desktop). The field, the Search button and the three sort tabs share one line at 1280 and 1920. The sort
  labels are shorter (`Newest`, `Cheapest`, `Priciest`; vi `Mới nhất`, `Rẻ nhất`, `Đắt nhất`). The unit basis stays visible in each price (`(45 each)`).
- **Phone (under 640 px):** the search field takes a line of its own (`flex-basis: 100%`), so its placeholder is not squeezed.
- **Listing rows:** the item picture is the compact 12 u slot (`ItemSlotButton Compact="true"`). The name, the quantity and the seller sit on one
  line with an ellipsis. The price and the buttons are unchanged.
- **My listings:** each of the player's own listings shows how long it has left (`N hours left` / `1 hour left` / `Expired`), from the expiry the
  server already sends. Local clock, so a skewed clock shifts the hour shown; the server still decides when a listing ends.

## Open (owner)

- **Eight rows without an inner scroll at 1280×720 and 1920×1080 (`ui-standard.md` section 7, item 7): not met.** The 14 u row buttons set the
  row height. One of: a smaller row button (a shared control; ui-standard keeps one button size), a page size per viewport, or a shorter header.
  The owner chooses; nothing here changes the button size.
- **The price of current asks** (critique section 6, item 8) needs a server query for open asks; a wire change, so for a later round.
- **The recurring late-game sink** (critique section 2) is still the owner's decision.

## Other findings in this pass

- **Master does not load in the browser.** `renderer.js` imported `fitLabel` from `label-fit.js` (c49a67e) and kept its own `fitLabel`, so the
  module failed to parse. The fix (commit 9a27c60) removes the local copy; the call site already uses the imported version.
- **Dev database:** the captures use a local PostgreSQL role `nosum` and the database `nosum_dev` (the dev defaults in the repo). These are the
  sandbox's local settings, not repository changes.

## Tests

- `dotnet build tests/Nosum.Tests.slnx --no-incremental`: **0 warnings, 0 errors**; `wasm/Nosum.Wasm`: 0 warnings, 0 errors.
- Unit tests (`tests/Nosum.UnitTests`): **3630 passed, 0 failed** (includes `Browsed_OwnListing_SaysHowManyHoursItHasLeft` and
  `Browsed_OthersListing_HasNoExpiryLine`).
- Integration tests (`tests/Nosum.IntegrationTests`): **827 passed, 1 failed** on the first full run. The failure was
  `VisitWireTests.Visitor_InTheOwnersFarmhouse_CanLookButNotPlaceMovePickUpOrShip` (a visitor's map instance). The same class then passed
  **11/11** on rerun, so it looks like a timing flake; it does not touch the market, the wire or the UI. Not investigated further.
