# QA captures: gathering round 2

Browser checks for [ADR-089](../../adr/ADR-089-gathering-review-tier-energy-and-feedback.md) (round-2 amendment) and the open items of
[critique-gathering.md](../../design/critique-gathering.md) section 7c. Before is `origin/master` at `ac55358`; after is the branch commit
`b69a432` (the XP floaters and the singular tooltip). Both at 1280×720, English and Vietnamese where the file says so. Names:
`before-<shot>-<vi|en>-1280x720.png`, `after-<shot>-<vi|en>-1280x720.png`, and `-close-` crops of the same frame.

## What the frames show

| Shot | Before | After | Verdict |
| :-- | :-- | :-- | :-- |
| Stump bar (`stump-hpbar`, `-close`) | the same trunk, no bar | 12 × 2 px bar, 6 of 12 px amber after one basic hit (node kind 2, max 2, one hit left) | Pass: the rule is correct for this node. The red 3 of 12 in the critique did not reproduce. |
| XP floater (`xp-floater-hit`, `-break`, `-stump-hit`, `-close`) | no label | green "+1 XP" on a hit, "+12 XP" on the breaking hit of a tree, "+7 XP" for forage; the energy "-3" stays below it | Pass for tree and forage, EN and VI. |
| Copper ore hit (`copper-ore-hit`) | no label | one "+1 XP" frame on a non-breaking 2-energy hit | Partial: the node was not identified and the three-hit cycle was not completed. |
| Tooltip, hoe (`tooltip-hoe`) | "Area: 1 tiles in a line" | "Area: 1 tile in a line" (EN); "1 ô liền nhau" (VI) | Pass. The plural fix is EN only; Vietnamese has no plural form. |
| Copper pickaxe tooltip (`copper-pickaxe-tooltip`) | — | "Energy per use: 2" | Pass. |
| Zero-energy toast (`zero-energy-toast`) | EN captured | EN "You are out of energy. Eat some produce, or wait for the next day." VI "Bạn hết sức rồi. Hãy ăn nông sản hoặc đợi sang ngày mới." | Pass for the after build. Before VI not captured. |
| Blacksmith (`blacksmith`, `-ordered`, `-collected`) | the order screen | order message "The smith is making your Copper axe." / "Thợ rèn đang làm Rìu đồng cho bạn."; after two game days "Ready! Take it back.", then "You took your tool back." | Pass on both builds, both languages. |
| Forage (`forage`) | no XP label | free pick (energy unchanged), row removed, "+7 XP" | Pass, EN and VI. |

## Grading against ui-standard.md section 7

- The XP label sits above the energy label and does not cover the node's bar or the top-right row; it is one short line at body size. Checked
  on the frames at 1280×720.
- The tooltip and toast lines are one line each. Checked as text on the frames.

## Still open

- **Copper ore break.** The mine-floor nodes are not in the database, so the full three-hit cycle is not captured. The "+1 XP" frame is not
  confirmed as copper ore.
- **Tree health bar.** The bar sits above the canopy, so the camera has to show it. Not framed in this run; the earlier tree capture is in
  [rebuild-gathering](../rebuild-gathering/README.md).
- **Before zero-energy toast in Vietnamese.** Not captured.
