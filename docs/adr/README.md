# Architecture decision records

One file per decision: `ADR-NNN-short-slug.md`. Numbers are never reused.

Each ADR has a status line (`**Status:** Proposed | Accepted | Superseded by ADR-NNN`),
Context, Decision (numbered `D1…Dn`, cited from code and docs), Alternatives considered,
Consequences, and Revisit when. An ADR written by an agent lands as `Proposed` and becomes
`Accepted` when the owner approves it. Add the row below in the same commit as the ADR.

| ADR | Title | Status |
|:--|:--|:--|
| [ADR-001](ADR-001-separate-engine.md) | Separate, platform-free engine | Accepted |
| [ADR-002](ADR-002-batched-render-interop.md) | Batched render interop: one JS crossing per frame | Proposed |
| [ADR-003](ADR-003-server-authoritative-websocket.md) | Server-authoritative game over one Nalix WebSocket | Accepted |
| [ADR-004](ADR-004-pixel-art-assets.md) | Pixel-art assets: grid, provenance, atlases built by a repo tool (amended by ADR-007) | Proposed |
| [ADR-010](ADR-010-internationalization.md) | Internationalization: vi + en, .resx, codes on the wire, i18n{} envelope | Accepted |
| [ADR-007](ADR-007-placeholder-content-16px.md) | Development uses unpacked Stardew Valley content on a 16 px grid (placeholder) | Accepted |
| [ADR-006](ADR-006-layered-character-customization.md) | Layered (paper-doll) character customization | Proposed |
| [ADR-009](ADR-009-postgresql-persistence-and-accounts.md) | PostgreSQL persistence and player accounts (guests, Gmail login, one session per player) | Accepted |
| [ADR-008](ADR-008-snapshot-movement-sync.md) | Movement sync: client-predicted intents, full-state snapshots at 10 Hz | Proposed |
| [ADR-011](ADR-011-inventory-farming-data-model.md) | Inventory, farming and quests: catalogues, atomic audited inventory, computed crop growth | Proposed |
| [ADR-012](ADR-012-maps-and-warps.md) | Maps and warps: one catalogue, exits as data, the server moves the player | Accepted |
| [ADR-013](ADR-013-economy-coins-escrow-market-trade.md) | Economy: coins, one atomic transfer, shipping bin, consignment market, direct trade | Accepted |
| [ADR-014](ADR-014-coops-and-looks.md) | Co-operatives (membership, shared warehouse) and player looks | Accepted |
| [ADR-015](ADR-015-private-instances-and-placeables.md) | Private per-player farm and farmhouse, Town and Backwoods, player-placed furniture (amends ADR-012) | Accepted |
| [ADR-016](ADR-016-ui-theme-and-hud-layout.md) | One pixel-art UI theme (shared controls, 9-slice frames) and one HUD dock-region layout | Proposed |
| [ADR-018](ADR-018-seasons-and-server-calendar.md) | Seasons and the server calendar: two-week seasons, crops by season, clock computed on the client | Proposed |
| [ADR-005](ADR-005-three-quarter-top-down-view.md) | 3/4 top-down view (layers, y-sorting, feet anchors) | Accepted |
