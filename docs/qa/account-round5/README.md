# QA: account and wardrobe, round 5 (look changes in order, shown at once)

Captured with `tests/browser/account-round5/capture.js` (Playwright, Chromium) against the Development server (PostgreSQL, `nosum_dev`).
Each run is one fresh guest at 1280x720, 1920x1080 and 390x844 (touch), in Vietnamese (default) and English. The guest gets a mail gift
(a shirt and a hat) from the database, claims it in the game, and then the wardrobe checks run. Decision record:
[ADR-120](../../adr/ADR-120-wardrobe-look-changes-in-order.md).

- `before/`: the client built from `master` at `707138e7`, before this change. The server is the same for both.
- `after/`: this branch.
- `report.json` in each folder: what the run found on the page and in the database, and the page-error count.

## What the run checks

| Step | What it does | Before (master) | After |
|:--|:--|:--|:--|
| `wear-shows-at-once` | Clicks Wear on the Clothes tab. Counts the visible Wear and take-off buttons at once (`atOnce`) and 150 ms later. | The wear shows at once in 2 of 6 (`1280x720-vi`, `390x844-en`); in the rest it is not there at once (`atOnce` still shows the bag full) and is there after 150 ms. | At once in 6 of 6. |
| `wear-server-state` | Reads the look and the bag from the database after the answer. | The shirt or the hat is worn (6 of 6). | The same (6 of 6). |
| `takeoff-then-wear-same-tick` | Clicks Take off and Wear for the other item in one tick. Expects the first item off and the other on. | Passes 6 of 6. The race did not reproduce on master. | Passes 6 of 6. |
| `skin-wear-skin-same-tick` | Chooses a skin, clicks Wear, chooses another skin in one tick. Expects the second skin and both items worn. | Passes 6 of 6. | Passes 6 of 6. |
| `page-errors` | Counts page and console errors. | 0 | 0 |

The `takeoff-then-wear-same-tick` and `skin-wear-skin-same-tick` steps click buttons through the DOM in one tick. In the mixed step one
of the buttons can be on the hidden tab; the point is the order the requests are sent in, and the server must keep it.

## Frames

- `after/<WxH>-<lang>-wardrobe-bag.png`: the Clothes tab with a gift in the bag, before any wear.
- `after/<WxH>-<lang>-wardrobe-worn.png`: the shirt worn, the hat still in the bag.
- `after/<WxH>-<lang>-wardrobe-race.png`: after the take-off and the wear in one tick.
- `after/<WxH>-<lang>-wardrobe-mixed.png`: the Look tab after the skin, wear and skin in one tick.
