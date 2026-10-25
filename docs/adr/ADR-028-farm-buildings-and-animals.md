# ADR-028 — Farm buildings and animals

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M3. Builds on ADR-015 (instances,
placeables), ADR-024 (outdoor placement), ADR-023 (energy), ADR-018 (days).

## Context

Crops alone make a thin farm. Coops and barns add daily care and steady produce. The placeholder content has the
buildings (`Buildings/Coop.png`, `Barn.png`), interiors (`Maps/Coop.tmx`, `Barn.tmx`) and animals
(`Animals/White Chicken.png`, `White Cow.png`).

## Decision

- **D1 — Buildings are large placeables** on the player's farm or co-op land only, built at a carpenter counter
  (Town or Mountain, ADR-026 style counter) for coins + wood + stone: coop (4 000, wood 300, stone 100), barn (6 000,
  wood 350, stone 150). Construction completes after two game days. Footprint, door tile and interior map are data.
- **D2 — Interiors are private instances** (`coop@<building id>`), entered through the building's door; access follows
  the owner of the land (player, or co-op members on co-op land; visitors per ADR-019).
- **D3 — Animals** are bought at the same counter (chicken 800, cow 1 500), named by the player (nickname rules of
  ADR-009 apply), live in a building with capacity (coop 4, barn 4). Each game day an animal that was fed (hay in the
  feeding bench, auto-filled from a silo later; for now the player puts fiber/hay in) produces: chicken → egg, cow →
  milk (milking with a pail tool). Petting once a day raises friendship (0–1000), which raises the chance of large
  produce.
- **D4 — Animals wander** inside their building on a server-chosen path updated every few seconds (cheap, per chunk
  subscribers only); outside grazing is out of scope.
- **D5 — Persistence.** Tables for buildings and animals; generated migrations. Daily production happens in the day
  rollover hook, lazily evaluated on read where possible.
- **D6 — Wire.** A new band slice `0x3100–0x31FF` (Farm band) for build/animal intents and pushes; animals use the
  chunk publisher.

## Consequences

New item kinds: animal products (egg, milk; ids 900–949) and tools (milk pail). Shipping prices are data.
