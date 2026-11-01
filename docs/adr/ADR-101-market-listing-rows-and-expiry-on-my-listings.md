# ADR-101: Market listing rows are compact, search and sort share one row, and My listings shows the time left

## Status

Accepted

Amends [ADR-013](ADR-013-economy-coins-escrow-market-trade.md) (the market page layout). Closes item 7 of
[`critique-economy.md`](../design/critique-economy.md) section 6 and works on the round-2 open item "six rows without a scroll", which is still open (see Consequences). Amends
[`ui-standard.md`](../design/ui-standard.md) section 3 (one item slot size) with one documented exception. No wire change:
`NosumProtocol.Version` does not move.

## Context

Round 2 ([`docs/qa/economy-round2/`](../qa/economy-round2/README.md)) left one check open: at 1280x720 six listing rows did not fit the market panel without an
inner scroll, and a page of ten scrolled. The cause was layout, not data: one listing row was an 18-unit item slot (54 px) with two text lines, and the search and
the sort each took a row of their own above the list. The round-2 README named three fixes; the owner asked for the first two together.

Section 6 item 7 of the critique: a seller cannot see when a listing ends (48 hours) without cancelling and relisting. The listing already carries its expiry
(`ListingRow.ExpiresAtMs`), so this is a view-model change.

## Decision

- **D1 — Compact listing slot.** `ItemSlotButton` takes `Compact`: 12 UI pixels (an 8 px picture, the same whole-sprite fit scale as the theme slot, scaled
  to 8 instead of 16). Only the market's listing rows use it. The default slot is still 18. This is the one exception to "one slot size" in
  `ui-standard.md` section 3; the picture stays whole and centred.
- **D2 — One line per listing.** A listing row is the compact slot, then "Name xN Seller: name" on one line, the price, and the button. A name or
  seller that is too long ends in an ellipsis; it does not wrap. The row is as tall as its buttons (14 u, the theme's button height).
- **D3 — Search and sort on one row.** The Browse tab's search field, Search button and sort tabs share one row (a `flex-wrap` box in `MarketPanel`).
  The field takes the room the other two leave (`flex: 1 1 0`, at least 40 u). The placeholder is shorter (`Search items`, `Tìm vật phẩm`) so it is
  not clipped at 1280 px.
- **D4 — My listings shows the time left.** Each of the player's own listings shows "N hours left" (rounded up) or "Expired", computed from
  `ExpiresAtMs` on the local clock. A listing lasts 48 hours, so minutes of clock skew do not change the hour shown. The text is only on the player's
  own rows (`ListingDisplay.ExpiryText`, empty for other sellers).
- **D5 — Phones stack.** Below 640 px the search field takes a full line of its own; the Search button and the sort tabs share the line below
  it. Nothing else changes. (Without this rule the three sort tabs and the field shared one line and the placeholder was cut.)

## Consequences

- **Not met: no inner scroll for a page of eight.** Measured with the capture script (`docs/qa/economy-round3/`): at 1280x720 the menu body
  needs 547 px and has 516 (31 px scroll); at 1920x1080 it needs 908 and has 751 (157 px scroll); at 390x844 it fits. The 14 u buttons in each row
  set the row height (42 px at 720, 70 px at 1080, where `--u` is 5), so the compact slot alone does not make the rows shorter. About seven rows would fit at
  720 and five at 1080 (computed from the measure). The fix is an owner decision: a smaller row button (a shared control, one size by ui-standard), a page size per viewport,
  or a shorter header.
- The compact slot is a second item picture size. A new screen that wants it must name a dense list in its review; the default stays 18.
- Expiry is the local clock. If a player's clock is far off, the hour shown is off by that much; the server still decides when a listing ends.
- Not done here: the price of current asks (section 6 item 8) needs a server query; the recurring late-game sink (section 2) is the owner's decision.

## Amendment 2026-11-01 (round 4): the page size follows the window

Resolves the open item "Not met: no inner scroll for a page of eight" above. The owner's decision (coordinator, round 4): keep the 14 u row buttons, and let the
page size follow the room the panel has. Amends the wire (protocol 33) and the browse page of [ADR-013](ADR-013-economy-coins-escrow-market-trade.md). No button
size and no panel size change; [`ui-standard.md`](../design/ui-standard.md) section 7, item 7 is met as written.

- **D6 — The page is the rows that fit.** The Browse page asks for as many listings as the listing list has room for. The room is measured with the shop's
  window measure (`shop-rows.js`, `measure`): the panel's max height less everything else in the body (the tabs, the search row, the price history, the
  pager). `MarketViewModel.RowsThatFit` turns it into whole rows: n rows take n × row + (n − 1) × gap, clamped to 1..20. The measure runs on each render of the
  Browse tab (a new reply, a tab change, the open) and on a debounced window resize, never per frame. A new size reloads the first page; the same size asks
  nothing, so the render that follows measures to the same answer and stops.
- **D7 — Wire: one field appended.** `BrowseListings.PageSize` (serialize order 3, `WireRange(0, 20)`; `WireLimits.MaxListingsPageSize` = 20). Zero is the
  server's default, 10 (`CoinRules.ListingsPerPage`). The server clamps a larger value to 20 (`MarketService.BrowseAsync`). Protocol 33; no opcode changed.
  Server and client ship together.
- **D8 — Only the newest browse is shown.** `MarketChannel` drops a reply to an older browse. Before this round a late reply could put back a page of the
  old size after the panel had asked for a new one, and the panel then stayed on the old size. The panel now shows the page it asked for last.
- **D9 — Buttons and the compact slot are unchanged.** The row buttons stay 14 u (D2, `ui-standard.md` section 8); the compact picture stays 12 u (D1).

**Result** (`docs/qa/economy-round4/`, eight listings per page replaced by the rows that fit; sixteen listings in the capture): at 1280×720 the page holds
seven rows and the body is `clientHeight == scrollHeight` (499 px); at 1920×1080 five rows, 668 px; at 390×844 fifteen rows of 28 px, 621 px. The same in vi and en.

Consequences:
- A short window shows fewer rows per page, so there are more pages. The count is the server's own, in the size asked; the pager reads "Page 1 of 3" at 1280×720.
- A resize reloads page 1 when the count of rows changes (a drag of the window asks once, after it stops).
- A client built for protocol 32 cannot browse on 33 (the field is appended to an existing packet, so they ship together).
- Not done: the price of current asks (critique section 6, item 8) still needs a server query for open asks.
