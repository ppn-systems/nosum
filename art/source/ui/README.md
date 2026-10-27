# art/source/ui/

The third-party sources of `assets/ui/` (`tools/art_repack/ui.py` reads them; `sources.py` pins each file by SHA-256). Both packs are CC0 1.0, by Kenney; the license
texts are kept in `docs/art/licenses/` and in each folder. Only the files the repack reads are committed; the full archives come from the pages in
[`docs/art/provenance.md`](../../../docs/art/provenance.md).

| Folder | Pack | Files kept |
| :-- | :-- | :-- |
| `kenney_ui-pack-pixel-adventure/` | UI Pack - Pixel Adventure 2.0 | `Tilesheets/Small tiles/Thin outline/tilemap_packed.png` (16 px cells: the window frame and the plaque) |
| `kenney_pixel-ui-pack/` | Pixel UI pack | `9-Slice/Ancient/` brown, grey and tan with their pressed frames, `9-Slice/Colored/` yellow and red with their pressed frames, `Spritesheet/UIpackSheet_transparent.png` (16 px cells with a 2 px margin: the close button and the cursors) |

Checked against other candidates (Kenney Fantasy UI Borders, Kenney UI Pack - RPG Expansion): the Pixel Adventure frames and the Pixel UI buttons are 16 px pixel art that matches the game's world pixel; the others are
smooth or dark-themed and were not used.
