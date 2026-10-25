# ADR-024 — Crafting, chests and placeables outdoors

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M2. Amends ADR-015 D2 (`MapSpec.Placeable`
now includes the farm and co-op land) and builds on ADR-021 (materials from nodes) and ADR-023 (skill unlocks).

## Context

Materials (wood, stone, fiber, coal, ore) exist once ADR-021 lands but have no use besides selling. Crafting turns
them into the objects that make a farm grow: storage, fences, paths, sprinklers, a furnace. Placed furniture
(ADR-015) already gives the server a per-instance placed-object store, footprints in the walk grid, and a client
mirror; outdoor objects reuse it.

## Decision

- **D1 — Recipes are data** in `Nosum.Gameplay` (`RecipeCatalog`): output item, quantity, inputs, unlock (skill +
  level, or "known from start"). Crafting is one intent; the server checks the unlock and does one atomic transfer
  (ADR-013) removing inputs and adding outputs. Crafting is possible anywhere from the inventory (no workbench in M2).
- **D2 — First recipes:** chest (wood 50), wood fence (wood 2), stone path (stone 1), scarecrow (wood 50, coal 1,
  fiber 20; farming 1), sprinkler (copper bar 1, iron bar 1; farming 2), furnace (copper ore 20, stone 25; mining 1),
  and three cooking recipes with `ItemKind.Food` outputs (salad, baked potato, pumpkin soup) through the same mechanism.
- **D3 — Outdoor placement.** The farm, co-op land and the farmhouse are placeable; public maps are not. Outdoor
  placement requires a free, walkable, non-tilled tile with no node; the same intents as furniture (place, move,
  pick up), so the client's arrange mode works outdoors too.
- **D4 — Behaviours** live in the server, keyed by item: chest (36-slot storage per placed chest; open/close intents;
  moves are atomic transfers between the player and the chest, audited like the co-op warehouse; only the owner and
  co-op members on co-op land may open), sprinkler (waters the 4 orthogonal tiles at day start — runs in the day
  rollover), scarecrow (no crow mechanic yet: decorative until M3 adds crows), furnace (5 ore + 1 coal → 1 bar,
  ready after 30 game minutes; collect intent), fence/path (blocking / decorative).
- **D5 — Ids:** materials and bars 500–599, crafted placeables 600–699 (food 700–799 per ADR-023).
- **D6 — Wire.** A new sub-range of the Placeable band (`0xB100–0xB1FF`) for craft, chest and furnace intents and
  pushes. Chest contents are sent when opened, not with the chunk state. Coordinator bumps the protocol version.
- **D7 — Client.** A crafting page in the game menu (grid of recipes, locked ones greyed with the unlock text, inputs
  with have/need counts); a chest page that shows the chest grid above the player's inventory (the Minecraft layout
  of ADR-017).

## Alternatives considered

- **Workbench-only crafting.** Adds a walk for no gain at this size. Revisit when recipes number more than ~40.
- **Chest contents in the inventory table with a container id.** Chosen variant of the existing inventory store: a
  chest is one more inventory owner (player, co-op warehouse, chest) — implementations should generalise the owner
  key rather than add a second store.

## Consequences

The day rollover gains work (sprinklers, furnaces); it must stay O(objects changed), batched per instance.
