# ADR-059: Monster Bestiary & Hunting Log in Journal UI

## Status

Accepted

## Context

With over 12 distinct creature and boss species spanning multiple underground tiers and wilderness zones, plus dynamic Elite champion affixes (ADR-058), players needed an in-game encyclopedia and reference manual. The bestiary provides vital intelligence on creature origins, health points, attack ratings, Combat XP yields, and primary/rare drop tables, reinforcing the core MMORPG exploration and dungeon progression loop.

## Decision

1. **Pure Data Model & Zero-Allocation UI**:
   - Modelled in `Nosum.Wasm.ViewModels.Progress.BestiaryEntryDisplay` as an immutable record containing monster name, category (Regular / Boss), location/floors, base HP, attack, XP, and drop tables.
   - Built statically from the authoritative catalog (`MonsterCatalog.All`) into `ProgressViewModel.Bestiary` ensuring zero garbage collector allocations per frame or UI re-render.

2. **Skills & Hunting Log Menu Integration**:
   - Rendered natively in `SkillsPanel.razor` within a dedicated `<GameSection Title="SỔ TAY SĂN BẮT QUÁI VẬT (MONSTER BESTIARY)">`.
   - Grid layout (`.bestiary-grid`) with responsive card components (`.bestiary-card`):
     - Standard monsters styled in deep slate panels with colored stat badges.
     - Milestone dungeon floor bosses highlighted with prominent gold borders (`.boss-card`), ambient golden luminescence, and warning badges.
     - Drop tables clearly separate primary drops from rare/legendary trophy drops (`.drop-item.rare`).

3. **Verification**:
   - Unit tests in `ProgressTests.cs` verifying complete catalog indexing and boss classification.
   - Headless Playwright browser validation capturing UI rendering and layout aesthetics in `docs/qa/`.

## Consequences

- Players have transparent, instantaneous access to enemy encounter stats and item drop locations without leaving the game or checking external wikis.
- Zero impact on hot-path server or client rendering performance.
