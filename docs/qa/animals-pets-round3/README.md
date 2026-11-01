# QA: animals and pets, round three (browser check of PR #300)

PR #300 (round two) shipped the produce sparkle after the day rolls over, the action-key preview (Feed, Collect, Pet), pet forage and the
carpenter's notice above the sections. None of it had been checked in a browser, because the capture flow never reached a placed coop. This
round fixes the capture flow, runs it on the PR #300 state (`before/`, commit `95092d8`) and on this branch after merging `master` (`after/`),
and grades the frames against [`docs/design/ui-standard.md`](../../design/ui-standard.md) section 7. Decision record:
[ADR-092](../../adr/ADR-092-animal-produce-cue-action-preview-and-pet-forage.md), amendment of 2026-11-02.

- Script: [`tests/browser/animals-pets-capture.cjs`](../../../tests/browser/animals-pets-capture.cjs). One guest per run, one language and one viewport.
- Build: server in `Development` on PostgreSQL; client built with `-p:NosumDevTools=true`, opened at `/?qa=1`.
- Frames: `before/<viewport>-<lang>/` and `after/<viewport>-<lang>/`. Viewports 1280 x 720 and 390 x 844; languages `en` and `vi`. Each run
  writes 31 frames; the key ones are copied here.
- The flow: grant coins and the coop blueprint; place it at farm tile (40, 40) and press E twice (the first press asks, the second builds); advance
  two game days; buy two chickens and a cat at the carpenter; walk in through the door (approach tile 39, 44) with hay in hand; feed (retried
  until the hay count in slot 9 drops); advance one day while still inside; walk toward the hens with the hoe; pet once a game day on the farm,
  first with an open bag, then with the bag full.
- Runs: `before` and `after` each ran four guests, two at a time (four at once made one toolbar wait time out).

## What was wrong, and what changed

| # | Found | How it was seen | Cause | Fix |
| :-- | :-- | :-- | :-- | :-- |
| 1 | The capture never placed a coop, so no animal section, no bench and no sparkle were drawn. | The old script's placement at farm (64, 20) was refused by the server ("Something is in the way") while the client ghost said it was clear. | The tile was not one the server accepts. | Placed at (40, 40) with the two-press build. The door approach (39, 44) was measured from the frames and checked by walking in. |
| 2 | After the day rolled over inside the coop, no hen showed produce or sparkle. Leaving and re-entering did show both. | Browser: `before/*/coop-new-day-sparkle` has no eggs and no gold sparkle; `after/` has both over the hens. | `AnimalsDecorator` read the game day from the local clock, so a server clock that moved on (the QA `advanceClock`) never triggered the room reload. | The day is read from the calendar's server clock (`CalendarChannel.ServerNowMs()`). |
| 3 | A petting that forages with a full bag found nothing on the grass, and the item was lost. | Code: the inventory add's result was ignored. Test: `PetWireTests.PetPet_WithAFullBag_LeavesTheForageOnTheGrassBesideThePlayer` asserts a drop row, which the old code never wrote. Browser, after: `bag-full` shows the bag at 36 of 36, and `pet-day-8-full-bag-world` shows the forage under the farmer. | A refused add vanished. | A refused add drops the item at the farmer's feet as a ground item caused by the farmer. The petting still counts. |
| 4 | On the second game day, the Pet button stayed off ("You already petted them today."), so no later petting was possible in the session. | Browser: in every `before/` run, the petting on days 2 to 8 times out on the disabled button (7 failures per run, the only failures in those runs). `after/` has none. | The pet was read once, at sign-in. | Opening the Pet panel reads the pet again (`Home.Show`). |
| 5 | The carpenter's purchase notice sits above the animal section and is not clipped at 1280 x 720. | `before/` and `after/` `carpenter-bought` are the same. | Not a regression: PR #300 already moved the notice above the section. | None needed; verified at both sizes and both languages. |

Forage in the database: `inventory_log` has `pet_forage` rows (the bag took it) and `ground_items` has forage rows caused by the farmer on `farm` at
(632, 719), which is tile (39, 44), the door approach. The roll is 30% per game day (`PetRules.ForageChancePercent`). A simulation of the same roll
over 32,000 consecutive days of time-prefixed guest ids gives 29.7%. The first `after` runs showed more forage than that on a small sample (16 of 32
pettings across four guests); that is within chance for so few days, and the unit test checks the roll.

## Grading (ui-standard section 7)

**Carpenter, Buildings tab, animal section and the notice (`carpenter-bought`, `carpenter-animals`)**

| # | Item | 1280 x 720 en/vi | 390 x 844 en/vi |
| :-- | :-- | :-- | :-- |
| 1 | In its region (centre modal) | Yes | Yes |
| 2 | Nothing overlaps | Yes: the notice sits above the section; the coop's two rows end inside the panel | Yes |
| 5 | `GamePanel`, S/M/L width | Yes | Yes |
| 6 | Three sizes, no word wrap, nothing clipped | Yes: the notice is whole in both languages | Yes |
| 7 | No inner scrollbar | Yes for the coop's two rows. The barn's rows were not captured | Yes |
| 9 | Spacing in `--u` | Yes | Yes |
| 10 | Vietnamese and English both fit | Yes for the text. **Open:** the duck row reads "Chuồng gà (2/4)" (critique finding 6: a content issue, not changed here) | Same |

**Coop interior: bench hint, produce sparkle, action-key preview (`coop-empty-bench`, `coop-new-day-sparkle`, `coop-hint-collect-*`, `coop-hint-pet`)**

| # | Item | Result |
| :-- | :-- | :-- |
| 1 | Region | Yes: the hints and the sparkle are engine-drawn and world-anchored (section 4). No Blazor card |
| 2 | Nothing overlaps | Yes at 1280 x 720 and 390 x 844: the labels sit in the room, clear of the quest tracker, the minimap, the hotbar and the touch controls |
| 6 | Text | English "Feed", "Collect" and "Pet" are seen in frames. Vietnamese "Thu hoạch" (Collect) is seen and fits. "Cho ăn" (Feed) is seen in the `before/390x844-vi` frame and fits; "Vuốt ve" (Pet) is not in a captured frame |
| 8 | Icons whole | Yes: the egg and the gold sparkle are whole and sit at the top-right of the hen |

The verb follows the press (ADR-092 D3): hay in hand at the bench says Feed; a hen with an egg in reach says Collect; a hen without one says Pet.

**Pet panel and the forage on the grass (`pet-day-*`, `pet-day-*-world`, `bag-full`)**

| # | Item | Result |
| :-- | :-- | :-- |
| 1 | Region | Yes: the forage is a ground item on the farm, drawn by the engine |
| 2 | Nothing overlaps | Yes: the forage lies under the farmer's feet, outside the HUD regions |
| 5 | `GamePanel` | Yes; the Pet panel keeps its title bar and close button |
| 10 | Languages | English: "Pet", "Give back" and "You already petted them today." fit. The Vietnamese pet panel was not reviewed in a frame |

## Not covered

- 1920 x 1080 was not captured: the brief asked for 1280 x 720 and 390 x 844.
- Barn rows, cows and goats: not captured; the coop has two species.
- Balance questions stay open: a chicken's payback (critique finding 7) and whether the pet's mood changes anything (finding 8).
- The duck row's "Chuồng gà (2/4)" label (finding 6) is a content fix for `Language.resx`, left for its own change.
- Walking into the coop is timing-based. The script retries the feed with small moves and reads the hay count, so a missed walk shows up as
  `feed` in the run's report, not as a silent gap. The `after/` runs all fed the bench (hay 30 to 6).
