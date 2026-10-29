# ADR-071: Furniture catalogue, seasonal stock and the decorating panel

**Status:** Proposed (agent-written, 2026-10-29; owner approval pending). Builds on [ADR-015](ADR-015-private-instances-and-placeables.md) (private instances and placeables), [ADR-018](ADR-018-seasons-and-server-calendar.md) (seasons), [ADR-024](ADR-024-crafting-and-outdoor-placeables.md) (placeables) and [ADR-027](ADR-027-weather-and-day-night.md) (night light).

## Context

The farmhouse held seven pieces of furniture, all sold by the furnisher, and no way to browse them. Placing a piece meant selecting it in the inventory and using the arrange keys; a player who owned several pieces had no catalogue. The night light existed for the floor lamp only.

## Decision

- **D1 — Thirty-two more pieces, same item kind.** Items 307 to 338 are `ItemKind.Furniture` with the stack of the first furniture (`FurnitureStack`), placed by `PlaceableCatalog` like the first seven. The art is cut from the Kenney packs already under `art/source/` and repacked into `sprites/furniture.png` below the original band (ADR-007, `assets-pipeline`). No new downloads and no new sheet.
- **D2 — A catalogue group on each piece.** `PlaceableDefinition.Category` (`FurnitureCategory`: seating, tables, beds, storage, lighting, plants, decor, rugs) sorts the pieces for the decorating panel. A piece with no group is not furniture.
- **D3 — Seasonal stock is a sale rule, not an ownership rule.** `PlaceableDefinition.SoldIn` names the only season the furnisher sells a piece in. `ShopRules.IsInSeason` applies it on the server (`ShopService`) and on the client (`ShopViewModel`), the same check seeds already use. Once owned, a seasonal piece can be placed in any season.
- **D4 — Light reuses the lamp rule.** A piece gives night light by its `LightRadius` (ADR-027 D5); the candelabra, table lamp, wall sconce and paper lantern set it. No lighting system is added.
- **D5 — Rugs, doormats and wall pieces do not block walking.** `BlocksWalking` is false for the rugs, the decor group and the wall shelf and sconce. Standing furniture still blocks.
- **D6 — The decorating panel reuses the placement flow.** The catalogue lists the owned furniture by group, with a search. Choosing a piece sets the held item, so the existing placement ghost (`PlacementActions`, `PlacedObjectsDecorator`) previews it; the pick-up button sends the existing `PlacementRequests.PickUp`. No new client state machine.
- **D7 — Wire unchanged.** No packet, opcode or field changes; the new pieces travel as item ids the client already knows from the catalogue. `NosumProtocol.Version` stays at 12, as ADR-066 D6 and ADR-019 D7 did for catalogue-only changes.

## Alternatives considered

- **A new packet for the furnisher's seasonal stock.** Rejected: the season rule runs from the shared catalogue on both ends, so no list has to travel.
- **Seasonal ownership (a piece leaves the room when its season ends).** Rejected: it would delete the player's property. Only the sale is seasonal.
- **Wallpaper and floor tiles.** Not built in this change: placeables are whole pieces on a footprint, and tile floors need a separate layer (a later ADR).

## Consequences

- The furnisher's stock grows from seven to thirty-nine pieces; the catalogue group, the season and the light radius are data on `PlaceableDefinition`, so a new piece is one table row, one item row and its art.
- The seasonal rule is one more branch in `ShopRules.IsInSeason`. A test pins which pieces are seasonal and that every season refuses the others.
- Placement validation is unchanged: the footprint, the reach and the free tiles of ADR-015 apply to every new piece.
