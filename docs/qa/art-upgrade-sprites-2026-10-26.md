# Sprite upgrade QA, 2026-10-26

Branch `feat/art-upgrade-sprites`. What changed and where it comes from: [`../art/provenance.md`](../art/provenance.md); candidates:
[`../art/sourcing-sprites-2026-10-26.md`](../art/sourcing-sprites-2026-10-26.md).

## Before and after

[`art-upgrade-sprites-contact-2026-10-26.png`](art-upgrade-sprites-contact-2026-10-26.png): the changed sheets, before on the left and after on the
right: items (seed packets in their crop colours), crops, barn, coop, tools, the scythe (weapons sheet, cell 7, 5), soil, chicken, cow, oak and the window frame.

## In the game (1280 x 720)

| Screenshot | Shows |
| :-- | :-- |
| [`art-upgrade-ingame-inventory-1280x720.png`](art-upgrade-ingame-inventory-1280x720.png) | Inventory open: packets that differ by colour and picture, produce, hay, milk, redrawn tools on the hotbar, nailed window frame |
| [`art-upgrade-ingame-crops-spring-1280x720.png`](art-upgrade-ingame-crops-spring-1280x720.png) | Spring field: parsnip, radish, cauliflower, potato, green bean, kale, garlic and strawberry, four growth stages each, on the new soil |
| [`art-upgrade-ingame-crops-summer-1280x720.png`](art-upgrade-ingame-crops-summer-1280x720.png) | Summer field: tomato, blueberry, melon, hot pepper, corn, wheat and sunflower, four growth stages each |
| [`art-upgrade-ingame-coop-exterior-1280x720.png`](art-upgrade-ingame-coop-exterior-1280x720.png) | The new coop beside the farmhouse |
| [`art-upgrade-ingame-coop-1280x720.png`](art-upgrade-ingame-coop-1280x720.png) | Three chickens inside the coop |
| [`art-upgrade-ingame-farmer-closeup.png`](art-upgrade-ingame-farmer-closeup.png) | The farmer facing down, right, up and left, cropped from the game at 5 x |

## How it was run

- Host `DOTNET_ENVIRONMENT=Development` with `NOSUM_CALENDAR_EPOCH` set to a daytime clock (spring day 2, and summer day 2 for the summer field),
  PostgreSQL 16 `nosum_dev`; client published with `-p:NosumDevTools=true` and served on port 5080; Chromium through Playwright; registered accounts
  (guests do not persist).
- Crop fields: planting and watering by hand, with `DevTools.AdvanceClock(n)`, grew crops unevenly, so the two field shots are laid out in
  `farm_plots` while the host was stopped: one crop per column, `growth_days` 1, 3, 6 and "ripe" down the rows, soil watered. The sprites the game draws
  for each stage are the real ones; only how the crops got there is staged.
- Coop: the blueprint came from `DevTools.Grant(610, 1, 0)` and was placed in the game; the building was marked complete and three chickens were
  inserted in `farm_buildings` and `farm_animals` while the host was stopped, because buying them needs the Town's carpenter. The player's
  `player_states` row was set to the tile below the door.
- Item shots: the items came from `DevTools.Grant`.

## Limits

- The coop interior, the animals' movement and the farmer layers' walk frames were not re-checked beyond these shots.
- Garlic, kale and bok choy are recolours of Tiny Farm turnips and cabbages, like the earlier recolours of the crops pack.
- The sheets not listed in the contact sheet (farmer layers, UI buttons and cursors, furniture) are unchanged except the farmer's forehead highlight.
