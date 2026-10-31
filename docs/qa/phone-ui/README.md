# QA captures: phone UI pass

Captured with `tests/browser/phone-ui-capture.cjs` (`before` on `origin/master` 5611626, `after` on this branch). The development
server runs on PostgreSQL (`DOTNET_ENVIRONMENT=Development`, database `nosum_dev`), the client is built with `-p:NosumDevTools=true`
and opened at `/?qa=1` as a guest. The Linux client build leaves out `assets/farmer/boots.png`, so the capture serves the repository's
`assets/` with `NOSUM_QA_REPO_ASSETS=1`. Languages: `vi/` (default) and `en/` (`lang=2`, set through the saved settings). Sizes: 1280x720,
1920x1080 and 390x844 (touch emulation). Each file is the full viewport.

Screens: `01-signin`, `02-hud` (with the hotbar), `03-inventory`, `04-tooltip-*` (speed tonic, boots, hoe or the Vietnamese equivalents),
`05-settings`, `06-merchant` (the seed keeper in Town). `audit-before.txt` and `audit-after.txt` hold the measured boxes and font sizes.

## What changed

1. **Tooltip overlap (390x844).** The description used the small size, which is 25 px at phone width, on an 11 px line. Lines overprinted and
   ran past the box. The description now uses the body size and the line height follows the size. Long lines wrap inside the box.
2. **Hotbar width (390x844).** The Z, X and V skill keys stack under the slots instead of beside them, so the hotbar and the energy gauges
   keep their margins. The touch joystick, the use and run buttons and the message line were moved up so none overlaps the taller hotbar.
3. **Phone text size.** Body text at 390 px was 12.5 px. It is now 14 px on a 17 px line (small text 12.5 px). The UI pixel stays 2 px.
   See the deviation below.

## Deviation from the request: 14 px, not a larger UI pixel

Body text is `--tp` × 12.5 px and `--tp` is a whole number of pixels, so the only body sizes are 12.5 px and 25 px. A UI pixel of 3 gives
25 px. That was tried: the 8-column bag, the HUD regions and the touch controls on a 390 px screen all overflowed (the bag and the hotbar
were cut at the screen edges, the quest card covered the coin badge, the joystick sat on the hotbar). Every grid and region would need a
phone layout of its own. Setting the phone body to 14 px keeps the measured layouts and meets the owners' minimum. The cost is that 14 px is not
a whole number of VT323 pixels, so the glyphs are not perfectly crisp. `docs/design/ui-standard.md` section 1 records the exception.

## Checklist (docs/design/ui-standard.md section 7), self-review

| # | Item | 390x844 | 1280x720 and 1920x1080 |
|:--|:--|:--|:--|
| 1 | Elements sit in their region | Yes: quest card top left, banner-free top centre, calendar and coin top right, hotbar bottom centre, joystick bottom left, emote and gauges bottom right | Unchanged |
| 2 | Nothing overlaps | Yes, checked in `02-hud`: the hotbar, the message line, the joystick and the buttons are clear of each other; the merchant box sits above the touch controls | Unchanged |
| 3 | One menu column, at most 8 buttons | Collapsed to the Menu button (unchanged) | Unchanged |
| 4 | Edges line up | Unchanged | Unchanged |
| 5 | Panels use `GamePanel` S/M/L/XL | Unchanged | Unchanged |
| 6 | Three font sizes, no word wrap, nothing clipped | Phone body 14 px (exception above); checked the longer English strings (sign-in, speed tonic) | Unchanged; the tooltip description at 1920 is body size (37.5 px) where it was small (25 px), in line with the rule |
| 7 | No inner scrollbar at 1280x720 and up | n/a (phones scroll a tall panel) | The inventory reports a `menu-page-body` overflow of a few pixels in the audit on master as well; no scrollbar shows in `03-inventory`. Not changed here |
| 8 | Icons whole and centred | Yes | Unchanged |
| 9 | Spacing in `--u` and gap tokens | Yes: the new hotbar rules use `--u` multiples | Unchanged |
| 10 | Vietnamese and English both fit | Checked both: English sign-in, speed tonic and merchant text fit | Checked both; no change |

The audits report no page errors in any run (`errors: none`).
