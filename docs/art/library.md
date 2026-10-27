# The art library

Open-licensed 16 px packs of any genre, kept in [`art/source/`](../../art/source/) so Nosum can use them ("we play with everything", owner decision). Only CC0 and CC-BY 3.0/4.0 packs are accepted; GPL-only, non-commercial, "free for personal use", CC-BY-SA and unclear licenses are rejected, and so is anything that imitates the early content's game. Each pack folder holds the sprite sheets, its license text and a `SOURCE.md` (URL, author, license, SHA-256 of the archive as downloaded, date). Previews, samples and engine files are dropped; the archive can be downloaded again from the URL and matched to the hash.

The art that reaches the game goes through `tools/art_repack` (`python3 tools/art_repack/repack_cc0_art.py --sources art/source`), which pins the SHA-256 of every sheet it reads and is listed per file in [`provenance.md`](provenance.md). A pack that is only in the library is not in the game. CC-BY packs must be credited: see Credits in `provenance.md`.

| Pack (`art/source/`) | Author | License | Contains | Used for |
| :-- | :-- | :-- | :-- | :-- |
| [`kenney_tiny-town`](https://kenney.nl/assets/tiny-town) Kenney Tiny Town 1.1 | Kenney (www.kenney.nl) | CC0 1.0 | Town tiles 16 px (132): terrain, houses, roofs, fences, trees, props, 12 columns | Barn, coop, farmhouse, greenhouse roofs and walls; fence icon |
| [`kenney_tiny-farm`](https://kenney.nl/assets/tiny-farm) Kenney Tiny Farm 1.0 | Kenney (www.kenney.nl) | CC0 1.0 | Farm tiles 16 px (132): crops in growth stages, seed bags, produce, hay, milk, eggs, barn boards | Crops, seed bags, produce, milk and eggs, barn and coop boards, pail |
| [`kenney_tiny-dungeon`](https://kenney.nl/assets/tiny-dungeon) Kenney Tiny Dungeon 1.0 | Kenney (www.kenney.nl) | CC0 1.0 | Dungeon tiles and 16 px heroes and monsters (132): walls, doors, chests, anvil, potions, weapons | Chest. Monsters and heroes are reserved for a future combat or mine content |
| [`kenney_tiny-battle`](https://kenney.nl/assets/tiny-battle) Kenney Tiny Battle | Kenney (www.kenney.nl) | CC0 1.0 | Tiny soldiers, vehicles, terrain (198 tiles, 16 px) | Not used yet |
| [`kenney_roguelike-rpg-pack`](https://kenney.nl/assets/roguelike-rpg-pack) Kenney Roguelike/RPG Pack | Kenney (www.kenney.nl) | CC0 1.0 | 1 700 tiles, 16 px with 1 px gap: terrain, trees, furniture, doors, windows, banners, tents, UI | Not used yet (furniture and tree candidates) |
| [`kenney_roguelike-characters`](https://kenney.nl/assets/roguelike-characters) Kenney Roguelike Characters | Kenney (www.kenney.nl) | CC0 1.0 | Layered 16 px character parts: bodies, outfits, hair, hats (genre-neutral) | Not used yet (character-base candidate: 16 x 16, not the 16 x 32 layout) |
| [`kenney_roguelike-indoors`](https://kenney.nl/assets/roguelike-indoors) Kenney Roguelike Indoors | Kenney (www.kenney.nl) | CC0 1.0 | Indoor tiles 16 px: floors, walls, furniture, kitchen, books | Not used yet (interior and furniture candidates) |
| [`kenney_roguelike-caves-dungeons`](https://kenney.nl/assets/roguelike-caves-dungeons) Kenney Roguelike Caves & Dungeons | Kenney (www.kenney.nl) | CC0 1.0 | Cave and dungeon tiles 16 px: rock, ore dust, stairs, water, doors | Not used yet (mine tileset candidate) |
| [`kenney_roguelike-modern-city`](https://kenney.nl/assets/roguelike-modern-city) Kenney Roguelike Modern City | Kenney (www.kenney.nl) | CC0 1.0 | Modern city tiles 16 px: roads, cars, shops, offices | Not used yet |
| [`kenney_rpg-urban-pack`](https://kenney.nl/assets/rpg-urban-pack) Kenney RPG Urban Pack | Kenney (www.kenney.nl) | CC0 1.0 | Urban RPG tiles 16 px: streets, houses, shops, cars | Not used yet |
| [`kenney_1-bit-pack`](https://kenney.nl/assets/1-bit-pack) Kenney 1-Bit Pack | Kenney (www.kenney.nl) | CC0 1.0 | 1 000+ tiles 16 px in a colour-keyed 1-bit style: fantasy, interior, urban, platformer | Not used yet |
| [`josehzz_farming-crops-16x16`](https://opengameart.org/content/farming-crops-16x16) Farming crops 16x16 | josehzz | CC0 | 20 crops, six pictures each (seed, three stages, ripe), 16 px | Crops without a Tiny Farm picture (strawberry, potato, grapes, ...) |
| [`dawnlike`](https://opengameart.org/content/dawnlike-16x16-universal-rogue-like-tileset-v181) DawnLike 16x16 Universal Rogue-like tileset v1.81 | DragonDePlatino | CC-BY 4.0 | CC-BY 4.0. 16 px with two animation frames: 400 creatures (animals, birds, undead, demons, slimes, humanoids, player races), 800 items (food, rocks, tools, weapons, armour, potions, scrolls), objects (trees, ore, doors, decor, traps, effects), floors, walls, GUI, fonts | Mushroom, hazelnut, berries, leek, soup, copper and steel ore, copper boulder icons |
| [`tiny16`](https://opengameart.org/content/tiny-16-basic) Tiny 16 (Basic, Dungeon, Overworld sheets) | Sharm (Lanea Zimmerman) | CC-BY 4.0 | CC-BY 4.0. Three 16 px sheets: basic tiles, dungeon, overworld | Not used yet |
| [`oga_tiny-creatures`](https://opengameart.org/content/tiny-creatures) Tiny Creatures 1.0 | Clint Bellanger | CC0 1.0 | About 180 16 px animals, monsters, birds, fish and fantasy creatures in the Kenney style (Tiny Town/Dungeon palette) | Chicken and cow |
| [`oga_trash-mobs`](https://opengameart.org/content/16x16-fantasy-rpg-trash-mobs-animated) 16x16 Fantasy RPG Trash Mobs (animated) | Emcee Flesher | CC0 | 16 px animated fantasy monsters (goblins, skeletons, orcs, ...) | Not used yet (monster candidates) |
| [`oga_animated-slimes`](https://opengameart.org/content/16x16-animated-slimes) 16x16 Animated Slimes | stealthix | CC0 | Animated 16 px slimes in three colours and two sizes | Not used yet (monster candidates) |
| [`oga_16x16-dungeon-tiles`](https://opengameart.org/content/16x16-dungeon-tiles) 16x16 Dungeon Tiles | ETTiNGRiNDER | CC0 | 80 single 16 px dungeon, effect and item tiles | Not used yet |
| [`oga_puny-dungeon`](https://opengameart.org/content/16x16-puny-dungeon-tileset) 16x16 Puny Dungeon Tileset v1 | Shade | CC0 | Dungeon tileset 16 px and sample characters | Not used yet (mine/dungeon candidate) |
| [`oga_assorted-rpg-icons`](https://opengameart.org/content/16x16-assorted-rpg-icons) 16x16 Assorted RPG Icons | Shade | CC0 | 16 px icons: armour, books, chests, consumables, potions, weapons | Not used yet |
| [`oga_weapon-rpg-icons`](https://opengameart.org/content/16x16-weapon-rpg-icons) 16x16 Weapon RPG Icons | Shade | CC0 | 16 px weapon icons in bronze, iron, steel and gold | Not used yet |
| [`oga_gold-treasure-icons`](https://opengameart.org/content/gold-treasure-icons-16x16) Gold treasure icons 16x16 | Bonsaiheldin | CC0 | 10 gold and treasure icons 16 px | Not used yet |
| [`oga_16x16-rpg-items`](https://opengameart.org/content/16x16-rpg-items) 16x16 RPG items | Jetrel | CC0 | A 16 px RPG item sheet | Not used yet |
| [`oga_random-animals`](https://opengameart.org/content/random-animals) Random animals | hdst | CC0 | Six 16 px animals (bee, fox, jellyfish, ladybug, parrot, pig) | Not used yet (new-animal candidates) |
| [`oga_pixel-animals-16x16`](https://opengameart.org/content/pixel-animals-16x16) Pixel Animals 16x16 | GrumpyDiamond | CC0 | A small 16 px animal sheet | Not used yet |
| [`oga_6-color-dungeon`](https://opengameart.org/content/6-color-dungeon-16x16) 6 Color Dungeon 16x16 | HorusKDI | CC0 | A six-colour 16 px dungeon tileset | Not used yet |

## Candidates not downloaded

Skipped for size or format: Ninja Adventure (pixel-boy, CC0, large download), Top Down Adventure Assets (ansimuz, 3 MB, mixed sizes), Tiny Top Down Pack (32 px), Forest Animal Pack (non-grid frames), CC0 Food Icons (32 px), RPG Tilesets pack (32 px and larger tiles). Rejected for license: CC-BY-SA sets (for example the Liberated Pixel Cup family) and every non-commercial pack.

## Adding a pack

1. Download the archive, record its SHA-256, copy the sheets and the license text into `art/source/<pack>/` (<= about 5 MB).
2. Write `SOURCE.md` next to them and add a row to the table above.
3. To use it in the game: add the pack to `PACKS` and its sheets to `SHEETS` in `tools/art_repack/sources.py` (pinned hash), copy its license to `docs/art/licenses/<pack>.txt`, map the cells in the relevant module, then run the script with `--docs`.
