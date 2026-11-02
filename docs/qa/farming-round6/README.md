# QA captures: farming round six (ADR-121)

Branch `claude/farming-round6` (from master `c0c4f584`). Script `tests/browser/farm-round6-capture.cjs` (Development server, client published with
`-p:NosumDevTools=true`, `?qa=1`, saved language vi or en). Each file is one frame: `<lang>-NN-<step>-<W>x<H>.png`.

- **after** = this branch (`after/`): 1280x720, 1920x1080 and 390x844, vi and en. The 390x844 set is the width-override copy (`Device.MinimumWidth` 320 in that
  copy's `appsettings.json` only; the product keeps 1024).
- No **before** set. The old line was `Farm_Status_OutOfSeason` ("That crop does not grow in this season."); it is still the fallback when the item in hand is not a seed.

| Step | Expected line (vi / en) | Result |
|:--|:--|:--|
| `tilled` (season moved to Summer, one hoe tap) | "Đã xới đất." / "Soil tilled." | pass, all sizes |
| `out-of-season-seed` (parsnip seed tap on the tilled tile) | "Cây này chỉ trồng được vào Xuân, Thu, Đông." / "That crop only grows in Spring, Fall, Winter." | pass, all sizes |

Order matters in the script: bare tilled soil reverts after three dry game days (`FarmRules.DryDaysToRevert`), so the season moves first and the till comes after.
