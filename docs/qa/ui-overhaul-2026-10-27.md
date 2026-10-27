# UI Assets Overhaul: Visual Redraw and Polish Report, 2026-10-27

Branch: `feat/ui-assets-overhaul`  
Author: Phan Nguyen  
Co-author: `Co-authored-by: phcnguyen <phcnguyenz@proton.me>`

---

## 1. Overview & Motivation

Nosum includes an extensive art asset library (`art/source/` with 26 CC0 / open-licensed asset packs such as Kenney Pixel UI Pack, Kenney UI Pack Pixel Adventure, DawnLike, and OpenGameArt icons). However, several major UI elements were previously rendered using rudimentary placeholders:
1. **Menu Tabs**: 12 monochrome ASCII strings (e.g. `BAG`, `OVR`, `TRD`, `V+`) with a harsh `filter: saturate(0.7) brightness(0.9)` washing them out.
2. **Calendar Seasons**: A flat 4x4 CSS square (`.season-icon`) with no seasonal character.
3. **Weather Display**: Hardcoded 8x8 SVG rects without depth or thematic feeling.
4. **Coin & Economy**: A plain 10x10 CSS circular glyph and non-tabular text.
5. **Quest Tracker**: Raw textual layout without the RPG parchment scroll identity.
6. **Hotbar & Item Slots**: Lacked physical bevel inset depth; hotkey pills hung uncomfortably below.
7. **Settings Sliders & Toggles**: Plain text labels without audio or UI iconography.
8. **NPC Dialogue**: Missing animated advance/prompt arrows for dialogue progression.

This overhaul replaces these crude placeholders with rich, cohesive pixel art generated directly from Kenney's color palette and open UI packs, wired seamlessly into `--nosum-img-*` CSS variables without any client runtime network overhead or runtime allocation.

---

## 2. Visual Proof: In-Game Screenshots

All screenshots below were captured from the live Chromium browser instance via Playwright running the published WebAssembly client and realtime server.

### Main Surfaces

| Surface | 1280 x 720 | 1920 x 1080 |
| :--- | :--- | :--- |
| **Farm HUD** (New season sprout, weather sun, 3D gold coin, quest scroll badge, and beveled hotbar slots) | ![HUD 1280x720](ui-overhaul-hud-1280x720.png) | ![HUD 1920x1080](ui-overhaul-hud-1920x1080.png) |
| **Game Menu: Inventory Tab** (All 12 vibrant tab icons, 3D inset item slots, farmer preview) | ![Inventory 1280x720](ui-overhaul-menu-inventory-1280x720.png) | ![Inventory 1920x1080](ui-overhaul-menu-inventory-1920x1080.png) |
| **Game Menu: Settings Sound Panel** (Music note, sound speaker, mute icons on sliders and toggles) | ![Settings 1280x720](ui-overhaul-menu-settings-1280x720.png) | ![Settings 1920x1080](ui-overhaul-menu-settings-1920x1080.png) |

### Detail Close-Ups (Native Pixel Scale)

| Component | Detail Screenshot | Description |
| :--- | :--- | :--- |
| **12 Menu Tabs** | ![Tabs Row](ui-overhaul-menu-tabs-1280x720.png) | Left-to-right: Leather Backpack, Blue Overalls, Locked Crate, Sprout, Coop Chicken, Straw-Hat Farmer, Twin Hearts, Sealed Envelope, Golden Star, Portrait, Steel Cog, Trophy. |
| **Calendar Clock & Weather** | ![Clock & Weather](ui-overhaul-clock-1280x720.png) | Pixel-art Spring blossom sprout, crisp sun icon with light rays, and next-day forecast preview. |
| **Quest Tracker Card** | ![Quest Card](ui-overhaul-quest-1280x720.png) | Sealed quest parchment scroll icon (`quest-scroll`) alongside bold quest title and reward. |
| **Hotbar Slots** | ![Hotbar Slots](ui-overhaul-hotbar-1280x720.png) | 1px inset dark shadow bevel for physical depth, high-contrast tool art, and crisp hotkey pills. |

---

## 3. Technical Implementation Details

### Asset Repacking (`tools/art_repack/ui.py` & `assets/ui/cursors.png`)
- Added dedicated 16x16 pixel generators using Kenney's UI palette for:
  - 12 Tab icons: `inventory` (rucksack), `wardrobe` (tunic), `trade` (arrows/crate), `visitors` (farmer), `market` (chicken/stall), `friends` (hearts), `mail` (wax-sealed letter), `skills` (gold star), `profile` (portrait), `settings` (steel cog), `goals` (gold trophy), `shop` (basket).
  - 4 Seasonal icons (`season_icon`): Spring (leaf & bloom), Summer (bright sunflower/sun), Autumn (golden oak leaf), Winter (crystalline snowflake).
  - 4 Weather icons (`weather_icon`): Sun, Rain, Storm, Snow.
  - UI functional icons: `quest_scroll`, `sound_icon`, `music_icon`, `dialog_arrow`, `coin_pouch`, and enhanced `coin_rich`.
- Placed clean sprites in row 384 of `cursors.png` (a previously unmapped, empty transparent area), preserving all existing 9-slice button and cursor definitions.

### Client Theme Wiring (`wasm/Nosum.Wasm.Controls/wwwroot/nosum-theme.js`)
- Registered new sprite definitions in the `SPRITES` coordinate map (`season-spring`, `season-summer`, `season-fall`, `season-winter`, `weather-sun`, `weather-rain`, `weather-storm`, `weather-snow`, `quest-scroll`, `sound-on`, `sound-off`, `music`, `dialog-arrow`, `coin-pouch`, and upgraded `coin` to 16x16 at `[240, 384]`).
- Published slices directly as base64 CSS variables (`--nosum-img-*`), eliminating any extra network round-trips.

### Razor Controls & CSS
- **`CalendarClock.razor.css`**: Updated `.season-icon` to 6u x 6u pixel art with background images mapped to `--nosum-img-season-*`.
- **`WeatherIcon.razor` & `.css`**: Replaced inline 8x8 SVG rects with `<span class="weather-icon @Kind"></span>` driven by `--nosum-img-weather-*`.
- **`QuestTracker.razor` & `.css`**: Integrated `<GameIcon Name="quest-scroll" />` on the title row.
- **`ItemSlotButton.razor.css`**: Added inner box-shadow bevels (`inset 0 1px 0 var(--nosum-edge-dark), inset 1px 0 0 var(--nosum-edge-dark)`) for tactile depth.
- **`GameMenu.razor.css`**: Removed washed-out `saturate(0.7)` filters, added interactive hover lift and active plate brightness.
- **`NpcDialogBox.razor` & `.css`**: Added pulsing `<GameIcon Name="dialog-arrow" />` continue indicator.
- **`SettingsPanel.razor`**: Enhanced sliders and toggles with optional `Icon` parameters (`music`, `sound-on`, `sound-off`).

---

## 4. Verification & Quality Gates

- **Compiler Warnings**: 0 warnings across all 8 `.slnx` under `TreatWarningsAsErrors`.
- **Frame Loop**: Zero allocations verified on hot render paths.
- **Automated Tests**:
  - `ThemeSpritesTests`: 2/2 green (verifies all sprite coordinates lie completely within sheet boundaries).
  - `Nosum.UnitTests`: 2,351/2,351 green (100% pass rate).
  - `Nosum.IntegrationTests`: 676/676 green with live PostgreSQL 16 database.
- **Browser Playwright Verification**: Executed automated browser script `tests/browser/capture-ui-overhaul.cjs` at 1280x720 and 1920x1080 without console errors.
