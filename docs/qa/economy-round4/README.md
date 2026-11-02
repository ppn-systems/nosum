# Economy round 4: captures

Captures of the market in English and Vietnamese at **1280×720**, **1920×1080** and **390×844**, from
`tests/browser/rebuild-economy/capture-market.js` against a Development build with the dev tools (`-p:NosumDevTools=true`).
Critique: [`critique-economy.md`](../../design/critique-economy.md) section 12.

- `after/<WxH>/<lang>-*.png`: the Browse, Sell and My listings screens. Each size is captured after the market is cleared
  (`TRUNCATE market_listings, market_sales`).
- `after/<lang>-report.json`: the capture report of the last size run; `errors` is empty (no page or console error).

## Before and after

This round changes no layout. The only visible value is the "hours left" text on My listings, which now counts on the server
clock; the screens above show the same layout as round 3 (`docs/qa/economy-round3/after`). The `before/` set is therefore not
re-taken.

## Skipped in the capture

- `buyer-browse-sorted` at 390×844: the harness found no sort control on the phone layout, so it skipped the step (the same
  as round 3's phone capture, where the sort tabs sit on a line below the search).
