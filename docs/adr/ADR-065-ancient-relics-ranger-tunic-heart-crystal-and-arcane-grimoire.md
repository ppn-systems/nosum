# ADR-065: Ancient Relics, Ranger Tunic, Heart Crystal, & Arcane Grimoire Crafting

## Status

Accepted — 2026-10-28

## Context

Inspection of `assets/sprites/items.png` (`384 × 80`, 120 cells of `16 × 16` icons) revealed six remaining unused MMORPG item sprites in row 4 (`y = 64`):

- Cell `104` (`col 8, row 4`): Ruby-bound Arcane Spell Grimoire.
- Cell `105` (`col 9, row 4`): Undead Necromancer Skull trophy.
- Cell `107` (`col 11, row 4`): Ornate 3-flame Sanctuary Silver Candelabra.
- Cell `110` (`col 14, row 4`): Engraved Ancient Runestone Tablet.
- Cell `111` (`col 15, row 4`): Agile Ranger Battle Tunic.
- Cell `112` (`col 16, row 4`): Radiant Crimson Heart Crystal.

At the same time, players progressing between Combat Level 1 (`LeatherArmor`, `+3` Defense) and Combat Level 3 (`IronChestplate`, `+7` Defense) benefit from a mid-tier agile chest piece (`RangerTunic`, `+4` Defense), as well as specialized high-tier combat consumables (`ArcaneGrimoire` for massive mana restoration + `Swiftness`, and `HeartCrystal` for burst vitality + enhanced `Regeneration`).

## Decision

1. **Extend `ItemCatalog` (`Nosum.Gameplay.Items`)**:
   - `RangerTunic = 407` (`"ranger_tunic"`, `ItemKind.Clothing`, `WearSlot.Shirt`, `SellPrice = 350`, `ObjectIcon = 111`).
   - `NecromancerSkull = 522` (`"necromancer_skull"`, `ItemKind.Resource`, `SellPrice = 450`, `ObjectIcon = 105`).
   - `RunestoneTablet = 523` (`"runestone_tablet"`, `ItemKind.Resource`, `SellPrice = 500`, `ObjectIcon = 110`).
   - `SanctuaryCandelabra = 524` (`"sanctuary_candelabra"`, `ItemKind.Resource`, `SellPrice = 950`, `ObjectIcon = 107`).
   - `ArcaneGrimoire = 713` (`"arcane_grimoire"`, `ItemKind.Food`, `foodEnergy: 40`, `SellPrice = 650`, `ObjectIcon = 104`).
   - `HeartCrystal = 714` (`"heart_crystal"`, `ItemKind.Food`, `foodEnergy: 50`, `SellPrice = 750`, `ObjectIcon = 112`).

2. **Extend `RecipeCatalog` (`Nosum.Gameplay.Crafting`)**:
   - `RangerTunic = 22` (`CraftSkill.Combat`, level 2): `LeatherArmor (1)` + `BatWing (2)` + `Fiber (5)` → `RangerTunic (1)`.
   - `ArcaneGrimoire = 23` (`CraftSkill.Combat`, level 3): `ManaCrystal (2)` + `SporeCap (2)` + `Wood (5)` → `ArcaneGrimoire (1)`.
   - `HeartCrystal = 24` (`CraftSkill.Combat`, level 3): `HealthPotion (1)` + `GolemCore (1)` + `SlimeGel (2)` → `HeartCrystal (1)`.
   - `SanctuaryCandelabra = 25` (`CraftSkill.Combat`, level 4): `IronBar (2)` + `ManaCrystal (2)` + `Coal (2)` → `SanctuaryCandelabra (1)`.

3. **Combat Rules & Paperdoll Integration (`CombatRules`, `PlayerActions`, `LookPalette`)**:
   - `CombatRules.ArmorDefense`: `RangerTunic => 4`.
   - `CombatRules.PotionRestore`: `ArcaneGrimoire => (20, 80)` and `HeartCrystal => (80, 25)`.
   - `CombatRules.PotionEffect`: `ArcaneGrimoire => (StatusEffectKind.Swiftness, 30, 1)` and `HeartCrystal => (StatusEffectKind.Regeneration, 40, 8)`.
   - `PlayerActions`: use `CombatRules.IsPotion(held.ItemId)` so all combat potions/relics (`HealthPotion`, `ManaPotion`, `Elixir`, `ArcaneGrimoire`, `HeartCrystal`) can be consumed directly in combat with the action key.
   - `LookPalette`: map `LeatherArmor`, `RangerTunic`, `IronChestplate`, `IronGreaves`, `IronHelmet`, and `BossCrown` to distinct paperdoll frames and tints so equipped battle gear is visible on the farmer sprite.

4. **Localization (`Catalogue.resx`, `Catalogue.en.resx`)**:
   - Add Vietnamese (default) and English names and descriptions for all 6 new items and 4 new crafting recipes.

## Consequences

- Every single MMORPG sprite cell in `assets/sprites/items.png` (`93..119`) is now mapped and active in gameplay.
- Players gain new mid/high-tier combat crafting recipes, consumable relics, and visible paperdoll armor appearances.
