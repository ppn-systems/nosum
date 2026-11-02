# Economy round 5: coin feedback captures

Captures of what the player reads after a coin moves, in English and Vietnamese at **1280×720**, **1920×1080** and **390×844**. Critique:
[`critique-economy.md`](../../design/critique-economy.md) section 13.

- `after/<WxH>/<lang>-buyer-shop-bought.png`: the seed shop after one seed bought. The line names the item, the quantity and the coins the server took.
- `after/<WxH>/<lang>-buyer-market-bought.png`: the market after a small buy. "Bought Parsnip x1 for 60 coins."
- `after/<WxH>/<lang>-seller-mail-collected.png`: the mail after "Collect all" on two gifts. One line for the run: "Collected 2 items and 160 coins."
- `after/<WxH>/<lang>-buyer-bought-large.png` and `-buyer-buy-confirm.png`: the large purchase (confirm, then the line): "Bought Parsnip x9 for 1,500 coins."
  Captured by `capture-market.js` with `LARGE_BUY=1`, as in round 4; the rest of its screens (browse, sell, sort) are the same as before this round.
- `after/<lang>-coins-<WxH>-report.json`: the text checks of the coin capture. Each check is the line the player must read, looked for on the page.
- `after/<lang>-report.json`: the report of the market capture (its last size run, as in round 4).

## Result

| Check | Result |
|:--|:--|
| Text checks (`capture-coins.cjs`: shop, market buy, mail run), 3 sizes × 2 languages | **18 of 18 found** |
| Page errors (both harnesses, all runs) | **none** |
| Captures | 72 PNG files (both harnesses, both languages, all sizes) |

Before this round the same moments said "Bought. It is in your inventory." (shop), "Bought." (market) and "Collected." for every mail gift. The layout is unchanged
apart from the status text, so the `before/` set is not re-taken (the round-4 rule). At 390×844 the shop line wraps to two lines inside the panel, with no overflow.

## Notes on the captures

- **Gift mails are seeded.** A market sale pays the seller at the sale and its mail is a notice with nothing to collect, so "Collect all" needs gifts. The coin
  capture inserts two `goal.reward` mails for the seller in the local dev database (120 coins; 40 coins and two parsnips), as `account-round2` seeds its mail.
  The seeding runs against the local development database only (the Development server's own).
- **Vietnamese item names.** The market check accepts any item name ("Củ cải vàng" is the parsnip in Vietnamese), so the check tests the amount and the
  quantity, not the word.
- **Harness fix.** `capture-market.js` looked for the sort buttons "Cheapest each" and "Priciest each". The build's labels are "Cheapest" and "Priciest"; the
  harness now matches them. Without the fix, the sorted browse and the large purchase were not reached.

## Reproduce

```
# server (Development, PostgreSQL role nosum, database nosum_dev)
DOTNET_ENVIRONMENT=Development dotnet run --no-build --project server/Nosum.Host
# tester client (ADR-079)
dotnet run --no-build --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080

# per language and size
NODE_PATH=$(npm root -g) BASE_URL=http://localhost:5080 OUT_DIR=docs/qa/economy-round5/after LANG_CODE=en VIEWPORTS=1280x720 \
  node tests/browser/rebuild-economy/capture-coins.cjs
NODE_PATH=$(npm root -g) BASE_URL=http://localhost:5080 OUT_DIR=docs/qa/economy-round5/after LANG_CODE=en VIEWPORTS=1280x720 LARGE_BUY=1 \
  node tests/browser/rebuild-economy/capture-market.js
```
