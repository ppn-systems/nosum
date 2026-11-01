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
