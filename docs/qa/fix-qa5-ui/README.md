# Fix pass for QA world pass 5 (UI)

Fixes the UI issues of `docs/qa/world-pass5/README.md` (ranked list, issues 1, 3, 5 to 11 and the low items). Before and after captures:
`before/` is `origin/master` at 8ef84b8; `after/` is this branch. Each has `{vi,en}/{1280x720,1920x1080,390x844}/NN-*.png` and a JSON of DOM measurements per
combination (`<locale>-<viewport>.json`). Capture script: `tests/browser/fix-qa5-ui-capture.cjs`.

## What changed, and the evidence

| Issue | Change | Evidence (before → after) |
|:--|:--|:--|
| 1 Phone: two column buttons | The minimap's map button is hidden on a phone; the Menu button is the column's only button and carries its name beside it. | 390 top-right: `Thư, Cài đặt, Bản đồ nhỏ, Menu` → `Thư, Cài đặt, Menu` (JSON `topRight`). |
| 3 Minimap after M | An M-key hide leaves no card and no button (`MinimapViewModel.ShowsButton`); the map button stays only for a map hidden in Settings. | 1280 and 1920, vi and en: card before M, none after M, card again after the second M (`02`–`04`). The old after-M state showed a map button in the corner. |
| 4 Harness opens phone panels | `world-pass.cjs` opens crafting, friends and skills from the Menu window's tiles; the minimap check measures visibility, not height. | Harness change; not rerun in full (see below). |
| 5 Settings scrolls at 1920 | The General tab is two columns on a desktop. | en 1920: `scrollHeight 785 / clientHeight 740` → no scroller (`05`). vi 1920 was 818/743 → no scroller. |
| 6 Phone touch targets and "Thêm" | Text buttons are 42 px on a phone (`--nosum-touch`); the window's icon buttons too; the stepper stays on one line. | Shop buttons 28 px → 42 px at 390 (`09`). "Thêm" stays on the stepper row. |
| 7 Clipped season label | Out-of-season rows say "Trái mùa" / "Off season"; the season list moves to the row's tip. | `ShopSeasonRowsTests` updated. The clipped text no longer occurs. |
| 10 NPC dialogs have no close | The merchant dialog box has a close cross (same action as Leave). | `10`: close control count 1 → 2 (close + Leave). |
| 11 Level-up over the dock | Desktop banners centre to the right of the furniture dock (margin 128 u). | The dock was not open in the capture, so the measurement shows no overlap but does not prove the case. **Not measured with the dock open.** |
| Low: quest header contrast | The quest header label uses the dark ink. | Visual only. |
| Low: Menu icon-only | The Menu button has its name beside it on a phone. | JSON `topRight` Menu 42×42. |
| Low: tooltip on touch | Hover tooltips are off on screens without hover, so a tap no longer leaves a tip behind. | Not captured; visual by code. |

## Not done, and why

- **Issue 8 (hint toast over the joystick):** the DOM shows the hint at y 630–684 and the stick at y ~732–828 on a 390×844 screen, so they do not
  overlap in this build. The QA screenshot may have shown a different state. Not changed; needs a screenshot with the hint on the joystick.
- **Issue 9 (opaque band over the bottom of the screen when a window is open):** not reproduced in the captures and not changed.
- **Low: co-op G opens the wrong panel:** G opens the Co-op window in both builds (title "Co-op" / "Hợp tác xã"). The wrong-panel frame of the QA
  pass was not reproduced; not changed.
- **Low: tooltip overlaps** (the combo chip at 1920): not changed.
- **Issue 6 partial:** the phone window close cross (`game-close`, 28 px) is a separate control of `GamePanel` and is not resized here.
- **Issues 2 and 14 (farm loop, season label):** not in this pass's list; not changed.

## Gates

- `dotnet build wasm/Nosum.Wasm.slnx`: 0 warnings (see the PR).
- `dotnet test tests/Nosum.UnitTests`: see the PR for the count.
- `node --test tests/browser/label-fit/*.mjs tests/browser/modules/*.mjs`: 40 pass.
- Full `world-pass.cjs` was not rerun after the harness change.
