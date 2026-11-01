# ADR-072: HUD minimap, world map and collection journal

## Status

Accepted

## Context

The player cannot see where they are in the wider world, nor what they have already found. Three UI pieces address that: a minimap in a HUD
corner, a world map panel that lists the public maps and how they connect, and a collection journal of every item the farmer has obtained.

The journal needs server state, because "discovered" must survive a reconnect and must not be decided by the client. The minimap and the world map
need no new server state: they read maps the client already loads. Two constraints bind all three. The wire is append-only (docs/adr/ADR-032,
nalix-packets), and the frame talks to the browser in one crossing each way (docs/adr/ADR-002), so a minimap may not add an interop call.

The keys for the new panels need input bits. The frame's `int` packs four held directions in the low bits and the edge bits above them
(`InputPacking`); the eight-bit held field left room for only three more edges, and the three keys wanted here (M, Tab, J) need three.

## Decision

- **D1 — The minimap is drawn by the engine into the same frame.** `MinimapLayer` (`engine/Nosum.Rendering`) caches the ground of a map once
  per map: one colour per tile from the topmost tile's traits (water, blocked, tilled, ground), merged into horizontal runs in preallocated
  arrays. Each frame it draws those runs and the dots (the farmer, other players, NPCs and exits) as colour rectangles in the command buffer, so
  it costs no crossing and allocates nothing. The box sits inside the minimap's HUD frame in the top-right corner (ADR-077 D1, which
  superseded the bottom-right corner of this decision). It is hidden on a narrow canvas (under 640 pixels) until the M key shows it.
- **D2 — The input layout narrows the held field to four bits.** The held directions use four bits, so `InputEdges.HeldBits` becomes 4 and
  `renderer.js` packs `pressed << 4`. Three edges are added: `Minimap` (M), `WorldMap` (Tab) and `Journal` (J). The packing is a shared contract
  (`InputPackingTests` mirror the JavaScript values).
- **D3 — The world map is a panel of the public maps.** `WorldMapViewModel` lists the six public maps (farm, town, beach, woods, mountain, the mine
  entrance) as cards, the current one highlighted. A card's connections are the targets of the warps of the map, which are known once the map is
  loaded; the loader prefetches the six maps in the background and a card shows "loading" until then. The game data has no shop opening hours, so
  the card says that they are not posted yet rather than inventing them.
- **D4 — The journal is server state, sent by request.** The server records a discovery in the transaction that grants an item: a row per
  player and item with a count and the first-obtained time (`player_discoveries`, migration `JournalDiscoveries`). The client asks with
  `GetJournal` (`0xD100` band) and receives `JournalReply`, which holds only the discovered entries. Every catalogue item is shown: a discovered
  one with its picture, count and date, an undiscovered one as a black silhouette with no count. `NosumProtocol.Version` becomes 17.
- **D5 — The art is reused.** No new art was drawn or downloaded. The world map and journal use two existing menu-tab pictograms (`tab-market`,
  `tab-goals`); the minimap uses colour rectangles. Map icons and frames can follow through `tools/art_repack` when someone draws them.

## Consequences

- The minimap's draw count grows with the ground's colour runs. A map of 16384 tiles or fewer is shown; larger maps show no minimap.
- The right-hand inset is a measured constant (96 CSS pixels from 1600 pixels wide), not a layout query. The engine cannot read the HUD layout,
  so a change to the HUD's system column needs this constant changed with it.
- A new key needs a bit in `InputEdges`, and the packing has room for four more edges (bits 27 to 30) before this ADR's layout is widened again.
- Discovery is written in the grant transaction, so every grant pays one extra row write, and the database budget tests count it.

## Amendments

### 2026-11-01 — merchants on the minimap (rebuild-world-ambience)

- An NPC that buys or sells (`NpcActions.Buy`) is drawn as a **white** minimap dot; an NPC who only talks stays orange. The rule is
  `MinimapMarkerRules.NpcColour` (pure, tested). Exits keep their purple dot. The markers are built once per map, so no per-frame cost.
- Evidence: `docs/design/critique-world-ambience.md` (section 3.2) and `docs/qa/rebuild-world-ambience/minimap-town-before-after-4x.png`.
