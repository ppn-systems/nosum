# Quest completion banner names the coins

Critique item 11 of `docs/design/critique-combat-fishing.md`: the balance rose by 50 coins on the first catch and nothing on screen said why.
The coins are the tutorial reward of `catch_fish` (`QuestCatalog`, 50 coins), paid by the server with the completion (ADR-036 D2). The fish
itself is not sold. While checking it, the banner was found not to show for most steps: the tracker showed the banner only when the whole chain
was finished (ADR-036 amendment).

## Before (the build with the old tracker, pre-fix)

A new guest tilled the soil (the first step, 10 coins). The server completed it (`player_quests` row `quest_id = 2`, `state = 2`, and a
`coin_log` row with reason `quest.reward`). The tracker did not show the banner. Trace of the quest panel, polled every 100 ms after one press
(a throwaway Playwright script, not committed; 1280x720, English):

```
   15ms QUEST[Till the soil / -]
  121ms QUEST[Water the soil / -]
```

The panel went straight to the next step; no `.quest.completed` element was ever present. The reward text on the banner did not exist yet.

## After

`tests/browser/quest-banner-capture.cjs` (tester build, Development server, `?qa=1`): teleport onto the farm row, press the hoe slot, press and
release on the tile below. Same six frames at 1280x720, 1920x1080 and 390x844, in Vietnamese and English:

| Frame | Size | Language |
| :-- | :-- | :-- |
| `after/quest-banner-1280x720-vi.png` | 1280x720 | vi |
| `after/quest-banner-1280x720-en.png` | 1280x720 | en |
| `after/quest-banner-1920x1080-vi.png` | 1920x1080 | vi |
| `after/quest-banner-1920x1080-en.png` | 1920x1080 | en |
| `after/quest-banner-390x844-vi.png` | 390x844 | vi |
| `after/quest-banner-390x844-en.png` | 390x844 | en |

Texts read from the frames: `Quest complete: Till the soil. You received 10 coins.` and `Hoàn thành nhiệm vụ: Xới đất. Bạn nhận được 10 xu.`
The coins line reads 510 in each frame (500 before the till), so the coins are visible on screen with the banner.

## Grading against `docs/design/ui-standard.md` section 7

| # | Check | Result |
| :-- | :-- | :-- |
| 1 | Element sits in its region | Yes. It is the quest panel's completed state, top left, where the quest card already sits. No new panel. |
| 2 | Nothing overlaps | Yes at all six frames. At 390 the text wraps to two lines inside the panel; the toast and the hotbar keep their places. |
| 3 | Menu column | n/a |
| 4 | Type and spacing | Same type and panel as the quest card (no new size). |
| 5 | Touch and small screens | 390x844 frames above; no horizontal scroll, no control added. |
| 6 | Language | Both languages from `.resx` (`Quest_BannerReward`); the English and Vietnamese lines are both checked above. |

Not changed here: the walking hint (`Walk with W A S D ...`) still sits on the scene during a cast and in the frames above (critique item 7).
