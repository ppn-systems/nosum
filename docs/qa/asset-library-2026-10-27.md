# Asset library QA, 2026-10-27

Branch `feat/asset-library`. The library: [`../art/library.md`](../art/library.md); what reached the game and from where: [`../art/provenance.md`](../art/provenance.md).

## What was swapped

| Sheet | Before | After |
| :-- | :-- | :-- |
| `sprites/animals/chicken.png` | drawn blobs | Tiny Creatures (CC0): one 16 px picture, flipped for the right row, bobbing on the walk frames |
| `sprites/craftables.png` | drawn chest | Tiny Dungeon (CC0) chest |
| `sprites/items.png` cells 50, 51, 91 | drawn rocks | DawnLike (CC-BY 4.0): copper chunk, steel-blue ore chunk, copper boulder |
| `sprites/items.png` cells 64, 66, 69, 72, 74 | drawn icons | DawnLike: soup bowl, leek, red berries, hazelnut, mushroom |

Not swapped, and why: the cow (Tiny Creatures' 16 px cow had to be doubled to fill its 32 px frame, which gives a 2 px outline that clashes with the chicken and the farmer, so the drawn cow stays); the farmer layers (no CC0 16 x 32 base with the same frame layout and tintable parts; Kenney's character pack is 16 x 16), trees and buildings (the current ones are larger and better matched than the 16 x 16 library trees), NPC sprites, `assets/ui` and the maps (other sessions), furniture (the placeable rectangles of `PlaceableCatalog` are bespoke views; the library's furniture is a candidate for a later pass). The game has no monsters yet, so the monster packs are library only.

## Before and after

[`asset-library-contact-2026-10-27.png`](asset-library-contact-2026-10-27.png): the changed sheets (items, chicken, trees, craftables), before on top, after below.

## In the game (1280 x 720)

| Screenshot | Shows |
| :-- | :-- |
| [`asset-library-ingame-coop-1280x720.png`](asset-library-ingame-coop-1280x720.png) | Three chickens in the coop |
| [`asset-library-ingame-barn-1280x720.png`](asset-library-ingame-barn-1280x720.png) | Three (drawn) cows in the barn with milk to collect |
| [`asset-library-ingame-mine-1280x720.png`](asset-library-ingame-mine-1280x720.png) | Mine floor 25: the copper boulder and the other rock nodes |
| [`asset-library-ingame-interior-1280x720.png`](asset-library-ingame-interior-1280x720.png) | The farmhouse with the new chest (left of the rug) and the furnace |
| [`asset-library-ingame-inventory-1280x720.png`](asset-library-ingame-inventory-1280x720.png) | Inventory: steel ore, mushroom, hazelnut, berries, leek, soup, chest, furnace and the copper ore in the hotbar |

## How it was run

Host `DOTNET_ENVIRONMENT=Development` with `NOSUM_CALENDAR_EPOCH` six hours before the run (game noon), PostgreSQL 16, client published with `-p:NosumDevTools=true` and served on port 5080, Chromium through Playwright, one registered account. Items came from `DevTools.Grant`. The coop, the barn, their animals and the farmhouse furniture were inserted into `placed_objects`, `farm_buildings`, `farm_animals` while the host was stopped (buying and placing them by hand needs the Town carpenter); the player's `player_states` row was set to the tile below each door, the mine floor's arrival tile and the farmhouse. The sprites drawn are the real ones; only how things got there is staged.

## Limits

- The chicken has one picture facing left: its down and up rows repeat it, so it shows its face walking up.
- The in-game credit for DawnLike is `wwwroot/credits.txt`; there is no credits screen yet.
