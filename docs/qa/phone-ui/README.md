# QA captures: phone UI pass

Captured with `tests/browser/phone-ui-capture.cjs` (`before` on `origin/master` 5611626, `after` on this branch, merged with `origin/master`
2a7686e). The development server runs on PostgreSQL (`DOTNET_ENVIRONMENT=Development`, database `nosum_dev`), the client is built with
`-p:NosumDevTools=true` and opened at `/?qa=1` as a guest. The Linux client build leaves out `assets/farmer/boots.png`, so the capture serves
the repository's `assets/` with `NOSUM_QA_REPO_ASSETS=1`. Languages: `vi/` (default) and `en/` (`NOSUM_QA_LANG=2`). Sizes: 1280x720,
1920x1080 and 390x844 (touch emulation). Each file is the full viewport.

Screens: `01-signin`, `02-hud` (with the hotbar), `03-inventory`, `04-tooltip-*` (speed tonic, boots, hoe or the Vietnamese equivalents),
`05-settings`, `06-merchant` (the seed keeper's dialogue box in Town), `07-shop-buy` (the seed shop opened from that box with Buy).
`audit-before.txt` and `audit-after.txt` hold the measured boxes and font sizes. `before/` is unchanged from the first pass.

## What changed in this pass (review failures at 390x844)

1. **Sign-in backdrop was flat mint.** Not the CSS and not the build: `art/own/ui/signin_background.png` is linked into the Controls
   package and is served (HTTP 200, `image/png`, checked in the built client). The capture's asset route only served `assets/ui/`, so the
   request for the backdrop returned 404 and the screen showed its base colour. The route now maps `signin_background.png` to `art/own/ui/`.
   The `after` frames show the sky and sea.
2. **Sign-in subtitle, notice and guest button text.** Measured in the built client at 390x844: the subtitle, the notice and the guest
   button all compute to **14 px on 17 px** lines, the same as the body (`html, body` at `--nosum-fs-body`). Nothing on the sign-in screen
   sets a smaller size. The text still reads small because VT323 at 14 px has small glyphs. The phone body size is capped at 14 px
   (`docs/design/ui-standard.md` section 1), so a larger size needs a phone layout change, not a font-size change. **Not changed here; to
   decide with the owners.**
3. **Hotbar split 5 + 4.** The phone rule built a 5-column grid. The phone hotbar is now one row of nine slots: each slot 14 u (28 px at
   390, the touch minimum in `ui-standard` section 6), a 1 u gap, the picture scaled to three quarters inside the slot (`zoom: 0.75` on
   `.icon`, so it stays whole and fits the 12 u inside the border). The Z, X and V skill keys stay under the slots. Measured: nine slots at
   the same top (the selected slot lifts 1 u), hotbar panel 296 px wide, energy gauge at x 311 to 331, nothing overlapping the message line
   or the touch buttons.
4. **Touch Chạy and Dùng.** The shared `.touch-controls` rule gave the use button and the run toggle `border-radius: 50%` and a 2 u
   bordered circle, which cut the shared button frame and its label. Both are now the shared `GameButton` faces: 14 u tall (`--nosum-button-h`),
   width from the label (`Dùng` 42 px, `Chạy` 42 px, label 22 px inside, no clipping), square corners. The run toggle stands 4 u above the use
   button. The latched run state (the page script adds `on`) wears the gold active face of every toggle. Measured in the built client:
   use `[332, 576, 42, 28]`, run `[332, 540, 42, 28]`, radius 0.

Also checked: the shop panel at 390x844 (Buy from the merchant box, `07-shop-buy`, both languages). The larger text fits: the title,
the balance, the presets, the quantity row, four offers and the detail strip are all inside the window; nothing is cut and no inner scrollbar
shows.

## Known and not changed

- **Shop at 1920x1080 scrolls by a few pixels.** At 1920x1080 the shop window is 900 px tall (the measured box) and its
  body is a few pixels taller, so `menu-page-body` scrolls and the last detail line sits at the bottom edge. This is the shop UX's
  layout on `origin/master` (the shop-ux pass added the detail strip) and the inventory shows the same `menu-page-body` overflow in the
  `before` audit. It breaks ui-standard checklist item 7 at 1920 and is left for the shop owners. The 390 and 1280 shop frames do not scroll.
- **Sign-in text size** (item 2 above) needs a decision on the phone body size.

## Checklist (docs/design/ui-standard.md section 7), self-review of `after`

| # | Item | 390x844 | 1280x720 and 1920x1080 |
|:--|:--|:--|:--|
| 1 | Elements sit in their region | Yes: quest card top left, calendar and coin top right, hotbar and skill keys bottom centre, stick bottom left, Use and Run bottom right, gauges right of the hotbar | Yes, unchanged |
| 2 | Nothing overlaps | Yes: checked `02-hud` and `07-shop-buy`; the hotbar, the message line, the stick and the touch buttons are clear | Yes; the shop window is inside the viewport at both sizes |
| 3 | One menu column, at most 8 buttons | Collapsed to the Menu button (unchanged) | Unchanged |
| 4 | Edges line up | Hotbar, gauges and the heart keep their margins; the touch buttons share a right edge at 8 u | Unchanged |
| 5 | Panels use `GamePanel` S/M/L/XL | Yes: the shop is `GamePanel`, one of the sizes | Unchanged |
| 6 | Three font sizes, no word wrap, nothing clipped | Yes: body 14 px (the phone exception), small 12.5 px; the touch labels and the hotbar keys fit; the shop title is 25 px and fits | Yes; the 1920 shop labels were flagged by the overflow probe, but the frames show them whole |
| 7 | No inner scrollbar at 1280x720 and up | n/a at 390 (phones scroll a tall panel); the shop does not scroll | 1280: no scroller in the shop or the HUD. 1920: the shop scrolls by a few pixels, see Known |
| 8 | Icons whole and centred | Yes: the hotbar picture is scaled to three quarters, whole and centred | Yes |
| 9 | Spacing in `--u` and gap tokens | Yes: the new rules use `--u` multiples | Unchanged |
| 10 | Vietnamese and English both fit | Yes: both languages checked on the sign-in, the HUD and the shop (the English detail lines are whole in the frames: "Total: 12 coins for 1" fits the window's right edge) | Both checked; the English 1920 shop title and labels fit |

The audits report no page errors in any run (`errors: none`).
