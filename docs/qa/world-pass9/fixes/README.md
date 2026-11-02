# World QA pass 9: fixes after PR #348

The issues of `docs/qa/world-pass9/README.md` that #348 did not fix. Each row says what this change does, how it was checked, and what is still open.
"Before" frames are the committed pass-9 captures; "after" frames were taken on this branch at 1280x720, 1920x1080 and 390x844 (vi and en where noted).

## Status

| # | Issue | Status | What changed | Evidence |
|--:|:--|:--|:--|:--|
| 4 | Co-op board at 1920 hides the column, minimap and hotbar | **Fixed** (minimap); hotbar hiding kept | The minimap card stays beside a window from 1600 px, as the menu column already did. The menu column was not hidden at 1920 (it is drawn beside the window in the pass-9 frame, so the report was out of date); the hotbar is still hidden while a window is open (`Home.razor`: a tall window such as the market reaches the hotbar, ui-standard section 2). | `before/coop-en-1920x1080-board-open.png`, `after/coop-en-1920x1080-board-open.png`; `tests/browser/world-pass9-fix-check.cjs` |
| 5 | Emote heart under the 8th column button at 1920; over the gauges at 390 | **Fixed** | Where the screen is tall (641 px and wider, 880 px and higher) the emote cell moves to the column's left, clear of the column's bottom. On a phone it stands above the two-row hotbar. | `after/inventory-en-1920x1080-in-hand.png` (emote beside the column), `after/farm-home-en-390x844.png` (emote above the gauges); checker: `1920 en: emote button is clear of the menu column` |
| 6 | Wardrobe worn list cut off at desktop; inventory "In hand" cut off at 1920 | **Wardrobe fixed; In hand partly fixed** | The wardrobe has two tabs, Look (skin, hair style, colour) and Clothes (worn and bag), one part at a time (ui-standard section 3). The inventory's hint line is one line in both languages. The In-hand strip is still cut off at the bottom at 1920: its stats line wraps and the strip needs one more line than the panel has. | `after/wardrobe-vi-1280x720-look-tab.png`, `after/wardrobe-vi-1920x1080-look-tab.png`; checker: the Clothes part fits at both sizes in both languages. In hand: open (layout change needed) |
| 7 | Buy-confirm dialog covers the market's filter row | **Fixed** | The confirm centres on the market window (125 u from the top, under the HUD row), not on the screen, so the search and sort row stays in view. On a phone it is full width and centred. | `before/market-buy-confirm-en-1920x1080.png`, `after/market-buy-confirm-en-1920x1080.png`, `before/market-buy-confirm-en-390x844.png`, `after/market-buy-confirm-en-390x844.png` |
| 8 | Save-account dialog has no close button | **Fixed on master by #348** (not re-captured here) | `AccountForm` passes a close control in upgrade mode (`GamePanel` renders it whenever `OnClose` is set). | Code: `wasm/Nosum.Wasm/Components/Account/AccountForm.razor` |
| 9 | Phone quest title loses its first letter under the scroll icon | **Open** | #348 gave the icon `flex: none`. In a probe at 390 wide the icon ends at x 46, and the first glyph's box is at x 50 to 56 with nothing over it, but a 1x crop still shows the "T" faint or missing. Needs a zoomed look and a decision. | probe only (no frame committed) |
| 10 | Furnace: collect feedback and the Copper button layout | **Fixed** | The furnace window is M wide, so "Smelt Copper ore" and its icon share a row. A successful collect says "Collected the bar." in the ok colour on the window's status line (the bottom message line is hidden while a window is open, so a toast would not show). A smelt start stays silent: the countdown speaks for it. | `after/furnace-en-1280x720-open.png` (the pass-9 frame, `bin/en/1280x720/06-furnace-place.png`, is not committed; the report describes it) and `after/furnace-en-1280x720-collected.png` ("Collected the bar." on the status line); unit tests `Collect_WhenItWorks_SaysOnTheWindowThatTheBarIsCollected`, `Start_WhenItWorks_SaysNothing_TheCountdownSpeaksForIt` |
| 11 | Mine harness does not reach floor 2; fight step trace | **Open** | Not traced in this pass. | — |
| 12 | Phone market: the seller name is cut ("QaF_") | **Fixed** | On a phone the listing's name and seller line takes the full width; the price and the actions sit on the line below. | `after/market-buy-confirm-en-390x844.png` (the listing behind the dialog reads "Parsnip x1 Seller: QaFriend7438") |
| 13 | Minimap card 10 px inside the top-right row at 1920 | **Fixed** (5 px at 1920, one unit) | The card is drawn one unit further out (its sprite has one transparent unit on the outside); the canvas map moves with it. Measured: the card's visible wood ends at x 1879, the same as the Settings and column frames. | `after/minimap-corner-en-1920x1080.png`; checker: `1920 en: minimap card is drawn one unit past the top-right row` |
| 21–23 | Clipped grids and lists; chat under the trade panel; "Profile" label wrap | **Not found** | These numbers are not in `docs/qa/world-pass9/README.md` or in this repository's QA docs, so I could not reproduce them from the text. Needs their descriptions. | — |
| 24 | Co-op log harness: silent `.catch` | **Fixed** | `tests/browser/social-round3-play.cjs`: the co-op log is a checked step (opens the co-op window if needed, clicks the Log tab, fails when it cannot). The Log tab's labels were added for vi and en. | `node --check` passes; not re-run end to end in this pass |

## Checks

- `node tests/browser/world-pass9-fix-check.cjs` (new): minimap beside a window at 1920 and hidden under one at 1280; minimap and column share the top-row edge (one-unit outset); emote clear of the column; the wardrobe Clothes part fits without an inner scroll at 1280 and 1920 in vi and en. Ten checks, all passing on this branch.
- `dotnet test tests/Nosum.UnitTests`: all pass (warnings as errors).
- `dotnet build wasm/Nosum.Wasm.slnx -warnaserror`: 0 warnings, 0 errors.
- `node --test tests/browser/label-fit/*.mjs tests/browser/modules/*.mjs`: 40 of 40.
- The QA harness (`tests/browser/world-pass8.cjs`) on this branch: coop en 1920x1080 (10 of 10), town vi and en at 1280x720 and 1920x1080 (8 of 8 each), market en 1920x1080 and 390x844 (8 of 8 each), bin en 1280x720 (9 of 9).

## Not covered by these captures

- The 390 px frames for the co-op, the wardrobe and the furnace, and vi frames for the market and the furnace (the furnace in vi was not run). The harness runs were the slow part; these are not claimed as checked.
- The account frame for issue 8 (code only).
- Issue 11 (mine) and issues 21 to 23 need their traces or descriptions before anyone fixes them.
