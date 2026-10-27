# Sprite sourcing: open-licensed 16 px farming art, 2026-10-26

A second search for better sprites than the first round in [`sourcing.md`](sourcing.md). Accepted: CC0, or CC-BY 3.0/4.0 with the attribution recorded.
Rejected: a license that is unclear, non-commercial, share-alike or GPL-only, a ban on redistributing the files, and anything that imitates another
game. Art direction: [ADR-040](../adr/ADR-040-own-art-plan.md) D1 (16 px, 3/4 top-down, 1 px dark outline). Where each file ends up:
[`provenance.md`](provenance.md).

Packs marked "downloaded" were unzipped into an empty directory outside the repository and opened as images only; nothing from an archive was run.
The licenses of the others were read on their pages (search result and page text), not from an archive.

## Result

| Package | Change | Source |
| :-- | :-- | :-- |
| Crops (`sprites/crops.png`) | 8 of 21 crops now use Tiny Farm stages (sprout, young, grown, harvest) instead of the crops pack's | Kenney Tiny Farm |
| Items (`sprites/items.png`) | seed packets redrawn on Tiny Farm's bag, each in its crop's colour with a picture of the harvest; 9 produce icons, hay, milk, brown egg, stone from Tiny Farm | Kenney Tiny Farm |
| Tools (`tools.png`, `weapons.png`) | hoe, axe, pickaxe, watering can and scythe redrawn in code (three-tone metal head with a glint, shaded handle, outline) in three tiers; milk pail from Tiny Farm | Nosum original, Kenney Tiny Farm |
| Buildings | barn walls and doors, coop boards, windows and door | Kenney Tiny Farm |
| Soil, trees, animals, farmer face, window frame | redrawn in code: furrowed soil, rim-lit tree crowns, wattle, comb and udder detail, a forehead highlight, a nailed window frame; same grids | Nosum original |
| Rest of the farmer layers, UI buttons and cursors, furniture | unchanged: no candidate fits the sheet layout and the style (reasons below) | - |

## Candidates

| Candidate | URL | License as verified | Author | Size | Fit |
| :-- | :-- | :-- | :-- | :-- | :-- |
| Kenney Tiny Farm 1.0 (downloaded) | https://kenney.nl/assets/tiny-farm | CC0, `License.txt` in the archive (2026-07-01) | Kenney | 16 px, 12 x 11 tiles, packed sheet | **Used.** Same palette and dark outline as Tiny Town, which the repository already uses. 6 crops with 5 tiles each, seed bags, tools, hay, buckets, barn walls, one chicken and two cows (single poses) |
| Kenney Tiny Town 1.1 (downloaded) | https://kenney.nl/assets/tiny-town | CC0 | Kenney | 16 px | Already used (axe, pickaxe, house and coop tiles, fence) |
| Farming crops 16x16 (downloaded) | https://opengameart.org/content/farming-crops-16x16 | CC0 on the page | josehzz | 16 px, 20 crops | Already used; now only for the 13 crops Tiny Farm lacks and for the seed pictures |
| Tiny Creatures (downloaded) | https://opengameart.org/content/tiny-creatures | CC0 on the page; collaborator Kenney | Clint Bellanger | 16 px, 10 x 18 tiles | Not used: dungeon monsters and animals in single poses; no walk cycle, no directions |
| LPC style farm animals (downloaded) | https://opengameart.org/content/lpc-style-farm-animals | CC-BY 3.0 or GPL 2.0 (the CC-BY option would be acceptable) | daneeklu | 128 x 128 chicken, 512 x 512 cow sheets, 3 to 4 directions, walk cycles | Not used (fit): realistic proportions about 50 px long; at 16 px they lose their shape, and they carry no outline |
| Gardens 16x16 Icon Pack | https://ivoryred.itch.io/gardens-16x16-icon-pack | CC-BY 4.0, free download | IvoryRed | 16 px, 80 tool icons in 4 tiers | Accepted by license, not used: Tiny Farm and Tiny Town cover the tool set, and a CC-BY file would add an attribution to carry. First choice if the tools are redone |
| Parchment GUI | https://opengameart.org/content/parchment-gui | CC0, "Copyright/Attribution Notice: None" | zwonky | 16 px panels, buttons, slots | Not used: parchment, not the wooden look; the window frame is sliced at 60 x 60 for `nosum-theme.js` |
| FreeRTP_UI | https://opengameart.org/content/cc0-ui-for-rpg-maker-vx-ace | CC0 on the page | (page) | grid not stated | Not used: made for RPG Maker VX, the grid and the slices do not match |
| Kenney Tiny Dungeon, Roguelike RPG, Pixel UI, UI Pack Pixel Adventure, Roguelike Characters, Puny Characters, Base character 16x16, Chicken Sprites, Farm spritesheet, 16x16 Food, CC0 Food Icons | see [`sourcing.md`](sourcing.md) | CC0 | various | | Reviewed in the first round; the verdicts there stand |
| Mini Farm (Jofra, mirrored by Nikuman Studio) | https://jofra.itch.io/mini-farm | CC0 on the page, but "no redistribution of individual files" | Jofra | 16 px, unfinished | **Rejected** (unclear: a CC0 grant cannot carry that condition) |
| PixelFarm (LushMustache) | https://lushmustache.itch.io/pixelfarm | CC0 tag; the free version has no buildings, the paid barn is 64 px | LushMustache | 16 px | Not used: nothing the free version adds |
| Farm Animals (ellen0ra) | https://ellen0ra.itch.io/farm-animals | CC BY-SA 4.0, paid | ellen0ra | 16 px | **Rejected** (share-alike) |
| Cow and sheep 16x16 | https://adamgamer1111.itch.io/cow-and-sheep-16x16 | CC BY-NC, paid | adamgamer1111 | 16 px | **Rejected** (non-commercial) |
| Ranch | https://tolee-mi.itch.io/ranch | CC BY-NC-SA 4.0 | Tolee Mi | 16 px | **Rejected** (non-commercial, share-alike) |
| farming crops (Tolee Mi) | https://tolee-mi.itch.io/farming-crops | not verified from the page | Tolee Mi | 16 px | Not used: license not confirmed; same author as the rejected Ranch pack |
| Farming Crops (Avos Grosimbourg) | https://avosgrosimbourg.itch.io/farmingcrops | credit required, redistribution banned | Avos Grosimbourg | 16 px | **Rejected** (no redistribution) |
| Crop Pack (Pixel Gnome), 32 Plants Set (PixelMoth), Farm crops (French Pixelle) | https://pixelgnome.itch.io/crops, https://pixelmoth.itch.io/32-plants-set, https://frenchpixelle.itch.io/farm-crops | paid packs with their own terms | | 16 px / 32 px | Not used: not verified as open licenses |
| Pixel Farm Items (Artimus) | https://lunaticartimus.itch.io/pixel-farm-items | may not be redistributed or resold | Artimus | 16 px | **Rejected** (redistribution) |
| Farm Tools Pixel Art Icon Set | https://shelbiewillhoite.itch.io/farmtools | "free to edit and use as you need": no named license | Shelbie Willhoite | 16 px, 70 icons | **Rejected** (unclear) |
| Pixel Lands Farm | https://trislin.itch.io/pixel-lands-farm | custom license, no redistribution | Trislin | 16 px | **Rejected** (custom license) |
| Rustic UI Pixel Art, Pixel UI Kit | https://chottoinc.itch.io/rustic-ui-pixel-art, https://gegx.itch.io/pixel-ui-kit | no resale or redistribution | Chotto Inc, GegX | 16 px | **Rejected** (redistribution) |
| Farmyard pack, Farm Essentials bundle | https://xstrax69.itch.io/farm-pack, gamedevmarket.net | paid, custom license | xStrax69, Maurizio | 16 px | **Rejected** (license) |

## Why the rest stays drawn, not cut from a pack

- **Animals.** Tiny Farm's chicken and cows are single poses and the drawn animals only got detail; the game needs three directions and four walk frames per animal, and the one CC0 or
  CC-BY set that has them (LPC) does not fit the 16 px style. The drawn animals keep their sheets.
- **Farmer.** Still no layered 16 x 32 CC0 base with a walk cycle (the first round's finding).
- **UI.** No CC0 wooden frame matches the slice sizes; the drawn frames stay.
- **Trees, furniture.** Tiny Farm's trees are 16 px tiles, the game's nodes are 48 x 96, so the trees are redrawn in code; furniture has no open source in this style.
