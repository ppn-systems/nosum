# Sprite upgrade QA, 2026-10-26

Branch `feat/art-upgrade-sprites`. What changed and where it comes from: [`../art/provenance.md`](../art/provenance.md); candidates:
[`../art/sourcing-sprites-2026-10-26.md`](../art/sourcing-sprites-2026-10-26.md).

## Before and after

[`art-upgrade-sprites-contact-2026-10-26.png`](art-upgrade-sprites-contact-2026-10-26.png): every changed sheet, before above after, at 2 to 3 x.

## In the game (1280 x 720)

| Screenshot | Shows |
| :-- | :-- |
| [`art-upgrade-ingame-inventory-1280x720.png`](art-upgrade-ingame-inventory-1280x720.png) | Inventory open: Tiny Farm seed bags, produce (parsnip, radish, kale, tomato, corn, wheat, sunflower, bok choy), hay, milk |
| [`art-upgrade-ingame-crops-1280x720.png`](art-upgrade-ingame-crops-1280x720.png) | Spring field on game day 10: parsnip, radish and cauliflower at several growth stages |
| [`art-upgrade-ingame-coop-1280x720.png`](art-upgrade-ingame-coop-1280x720.png) | Three chickens inside a built coop |

## How it was run

- Host `DOTNET_ENVIRONMENT=Development` with `NOSUM_CALENDAR_EPOCH` set to a daytime clock, PostgreSQL 16 `nosum_dev`; client published with
  `-p:NosumDevTools=true` and served on port 5080; Chromium through Playwright; registered accounts (guests do not persist).
- Crops: planted and watered with the hoe, seeds and can; `DevTools.AdvanceClock(n)` skipped game days (only watered days grow a crop).
- Coop: the blueprint came from `DevTools.Grant(610, 1, 0)` and was placed in the game; the building was marked complete and three chickens were
  inserted in `farm_buildings` and `farm_animals` while the host was stopped, because buying them needs the Town's carpenter. The player's
  `player_states` row was set to the tile below the door.

## Limits

- The field shot shows the spring crops that the starter seeds give (parsnip and radish from Tiny Farm, cauliflower from the crops pack). The Tiny Farm tomato, corn and wheat sprites
  are not in a game screenshot: they are summer crops, and the run did not reach summer.
- Animals, farmer layers, UI, trees, furniture and the coop wall are unchanged.
- Garlic, kale and bok choy are recolours of Tiny Farm turnips and cabbages, like the earlier recolours of the crops pack.
