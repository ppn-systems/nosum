# QA captures: rebuild-gathering

Before and after captures for ADR-089 and the gathering critique (docs/design/critique-gathering.md). Names: `before-<shot>-<vi|en>-<1280x720|390x844>.png`
and `after-…`. Before is master `384cb1d`; after is the branch commit `c0dfdd6` (the state before the merge of master `6f3784a`, see below).

## What the frames show

| Shot | Before | After |
| :-- | :-- | :-- |
| Tree with a hit | no mark on the trunk | 12 × 2 px bar above the trunk, filled by hits left (checked: `after-tree-hpbar-close-en-1280x720.png`) |
| Tooltip, basic and copper axe | damage line only | energy per use line added (3 basic, 2 copper); quoted in the report |
| Level-up banner | name and level | second gold line "Foraging tools: 1 less energy per use" at level 4 (checked: `after-levelup-banner-en-1280x720.png`) |
| Skills page, HUD with menu column | unchanged | unchanged (no change by design) |
| Phone gate, 390×844 | "widen to 1024 px" | the same gate; the 390 HUD frames were taken with a scratch override of the minimum width to 320 |

## Grading against ui-standard.md section 7

- Checked by the reviewer on the frame itself: the banner at 1280×720, EN. One region, no overlap with the top-right row or the minimap, the
  perk fits on one line, body size, gold on the banner frame: yes on items 1, 2, 6 and 10 (EN).
- Graded from the capture agent's report, not frame by frame: the tooltip lines, the health bar colours and sizes, the Vietnamese strings, the 390
  frames. These are open for a second look before merge.
- Failing item, not caused by this change: section 7 item 7 and the phone rule. At 390×844 the game blocks play behind the 1024 px gate (finding G8
  in the critique). No layout fix here can satisfy the phone rules until the owner decides on the gate.

## Known open items

- Stump bar: after the first hit on a stump the agent saw a red bar of 3 of 12 px. The code predicts amber, 6 of 12 px (stump max 2, hits 1). The
  server state was right (the next swing broke it). Not resolved in this wave; the frame needs a targeted look.
- Copper ore and forage were not re-measured after the change; the blacksmith was not re-run (its code is not in the diff).
- The AFTER set is from commit `c0dfdd6`. The merge with master's wide-use change (`6f3784a`) touches farm code only and was not re-captured.
