# Sourcing the non-terrain art from CC0 packs

Decision input for the owner's PixelLab budget ([ADR-040](../adr/ADR-040-own-art-plan.md), packages 1 and 3–9; terrain, packages 2 and 11, is another branch).
Each candidate below was downloaded into an empty directory outside the repository, its license read (inside the archive for Kenney, on the asset
page for OpenGameArt and itch.io) and its sheets opened as images only; nothing from an archive was run. A pack is **rejected** on any license other
than CC0, and on a license that is unclear, however well it looks. The art direction is ADR-040 D1: 16 px grid, 3/4 top-down, warm farm look, a 1 px
outline in a darker tone of the fill.

Result: 26 of the 27 non-terrain files are replaced. No layered CC0 base fits the farmer, so its 5 layer files are drawn in code like the animals; only `Maps/Mines/mine.png` is left, it goes with the mine maps. How each file was made is in
[`provenance.md`](provenance.md); the script that rebuilds them is [`tools/art_repack/`](../../tools/art_repack/).

## Candidates checked

| Candidate | URL | License as verified | Fit |
| :-- | :-- | :-- | :-- |
| Kenney Tiny Town 1.1 | https://kenney.nl/assets/tiny-town | CC0 1.0, `License.txt` in the archive | **Used.** 16 px, dark brown outline, warm palette: tools, house tiles, fence. Its palette is the shared palette of the drawn pictures |
| Kenney Tiny Dungeon 1.0 | https://kenney.nl/assets/tiny-dungeon | CC0 1.0, `License.txt` in the archive | Same palette. Swords, potions, chests, barrels. Not used: nothing in it is a farm item |
| Kenney Roguelike RPG Pack | https://kenney.nl/assets/roguelike-rpg-pack | CC0 1.0, `License.txt` in the archive | Flat, light outline, 1 px margin between cells: clashes with the dark outline. Has furniture, rocks, plates, bookcases; not used, the drawn pictures match the palette better |
| Kenney Pixel UI pack | https://kenney.nl/assets/pixel-ui-pack | CC0 1.0, `License.txt` in the archive | Flat coloured panels and arrows; no warm wood look. Not used |
| Kenney UI Pack - Pixel Adventure 2.0 | https://kenney.nl/assets/ui-pack-pixel-adventure | CC0 1.0, `License.txt` in the archive | Wooden frames in a warm palette, the closest to the game's window look; thin 16 px tiles that would need re-slicing to the 60 × 60 and 16 px slices `nosum-theme.js` reads. Not used; a candidate to polish the UI with |
| Kenney Roguelike Characters 2.0 | https://kenney.nl/assets/roguelike-characters | CC0 1.0, `License.txt` in the archive | Layered (body, clothes, hair) but 16 × 16, one or two poses, no walk cycle and no directions: cannot make the 16 × 32, 3-direction, 3-frame farmer |
| Kenney Roguelike Modern City 2.0 | https://kenney.nl/assets/roguelike-modern-city | CC0 1.0, `License.txt` in the archive | Modern city, wrong setting. Not used |
| Farming crops 16x16 (josehzz) | https://opengameart.org/content/farming-crops-16x16 | CC0, stated on the page (the archive has only a README) | **Used.** 20 crops, 5 growth stages and a harvest picture each, dark outline in the same family as Kenney's |
| Puny Characters (Shade) | https://opengameart.org/content/puny-characters | CC0 on the page | Whole characters about 16 px tall on a 32 × 32 canvas, chibi proportions, no hair, hat, shirt or trousers layers: no |
| Base character spritesheet 16x16 (Cough-E) | https://opengameart.org/content/base-character-spritesheet-16x16 | CC0 on the page | One naked 16 px base (128 × 48 file), not layered: no |
| Chicken Sprites (Shepardskin) | https://opengameart.org/content/chicken-sprites | CC0 on the page | The archive holds only GIFs at 2× and 4×, no cow: not used |
| Farm spritesheet | https://opengameart.org/content/farm-spritesheet | CC0 on the page | Isometric: wrong perspective |
| 16x16 Food | https://opengameart.org/content/16x16-food | CC0 on the page | Not downloaded: one author's mixed-quality icons; the crop pack already gives the produce |
| CC0 Food Icons (AntumDeluge) | https://opengameart.org/content/cc0-food-icons | CC0 on the page | Not downloaded: a collection of icons from several sources, so the license of each part is not that of the page |
| LPC style farm animals | https://opengameart.org/content/lpc-style-farm-animals | CC-BY 3.0 and GPL on the page | **Rejected** (license) |
| Mini Farm (Jofra) | https://jofra.itch.io/mini-farm | Page says CC0 but forbids redistributing individual files | **Rejected** (unclear: a CC0 grant cannot carry that condition) |
| Ranch (Tolee Mi) | https://tolee-mi.itch.io/ranch | CC BY-NC-SA 4.0 | **Rejected** (license) |
| Farm pack (xStrax69) | https://xstrax69.itch.io/farm-pack | Custom license, paid | **Rejected** (license) |

## Plan per package

| Package | Result | Source | Left to do |
| :-- | :-- | :-- | :-- |
| 1 Player farmer layers (5 files, 71 cells) | Replaced | No CC0 source: drawn in code (`tools/art_repack/farmer.py`): body and arms with three walk frames per direction, trousers, 8 hairstyles, 4 shirts, 2 hats, in the Kenney palette with a 1 px outline. Body, trousers and hair are light or grey-scale so the code's skin, pants and hair tints work. The shirt and hat inventory icons ride on `shirts.png` and `hats.png` | Optional polish: PixelLab, 19 jobs by ADR-040 D5 (about 40 with re-rolls), only if the budget is decided; the layers must keep the same cells and anchors |
| 3 Items (3 files, 141 cells) | Replaced | Produce: CC0 crops pack. Fence: Tiny Town. Seed packets, forage, food, animal products, resources, shells, furniture, scarecrow, chest, furnace: drawn in the Kenney palette | Optional: redo the drawn icons with PixelLab, 135 jobs or about 9 batched (`create_tiles_pro`) |
| 4 Crops and soil (2 files, 145 cells) | Replaced | CC0 crops pack, recoloured for the 9 crops it lacks; soil drawn | None |
| 5 Tools and weapons (2 files, 15 cells) | Replaced | Tiny Town axe, pickaxe, pail, sickle, tiers by palette swap; hoe and watering can drawn | None |
| 6 Resource nodes (5 files, 82 cells) | 4 of 5 replaced | Oak, maple, pine, stump, bush, sapling, seed drawn; rocks and ore drawn in the object sheet | `Maps/Mines/mine.png` holds the mine floors (package 11) as well as the ladder, so it goes with the mine maps; Tiny Town tile 103 (a ladder in a stone frame) can be the ladder cell (3, 4) |
| 7 Buildings and props (5 files, 246 cells) | Replaced | Farmhouse, barn, coop, greenhouse roof and walls from Tiny Town tiles; glass wall and shipping bin drawn | Optional: PixelLab `create_map_object`, 5 jobs, for buildings that look less modular |
| 8 Animals (2 files, 24 frames) | Replaced | No CC0 source: drawn (chicken, cow, three directions, four walk frames) | Optional: PixelLab character and walk, 4 jobs |
| 9 UI (4 files, 118 cells) | Replaced | Drawn to the slice sizes of `nosum-theme.js`; start picture drawn | Optional polish from Kenney UI Pack - Pixel Adventure |

Nothing is left that needs PixelLab. Everything is replaced with CC0 or drawn art; redoing the drawn pictures (the farmer, 19 jobs, about 40 with re-rolls) with PixelLab would cost about 170 jobs (135 item icons one by one, or 9 batched; 5 buildings; 4 animals; trees and UI by
hand) and is a quality choice, not a requirement.

## Limits of this result

- The crop and produce pictures of 9 crops (radish, garlic, cauliflower, kale, blueberry, hot pepper, cranberries, yam, bok choy) are recolours of look-alikes in the
  CC0 pack, so they are close to, not the same as, the vegetable's name.
- The drawn pictures were checked in the game at 1280 × 720 (inventory, hotbar, farm, soil, crops, UI windows, start screen); see the pull request.
- Whether AI-authored pictures in this folder need a note in the release license check is the same open question as ADR-040 for PixelLab output.
