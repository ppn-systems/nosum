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
| [ADR-005](ADR-005-three-quarter-top-down-view.md) | 3/4 top-down view (layers, y-sorting, feet anchors) | Accepted |
| [ADR-006](ADR-006-layered-character-customization.md) | Layered (paper-doll) character customization | Proposed |
| [ADR-007](ADR-007-placeholder-content-16px.md) | Development uses unpacked Stardew Valley content on a 16 px grid (placeholder) | Accepted |
| [ADR-008](ADR-008-snapshot-movement-sync.md) | Movement sync: client-predicted intents, full-state snapshots at 10 Hz | Proposed |
| [ADR-009](ADR-009-postgresql-persistence-and-accounts.md) | PostgreSQL persistence and player accounts (guests, Gmail login, one session per player) | Accepted |
| [ADR-010](ADR-010-internationalization.md) | Internationalization: vi + en, .resx, codes on the wire, i18n{} envelope | Accepted |
| [ADR-011](ADR-011-inventory-farming-data-model.md) | Inventory, farming and quests: catalogues, atomic audited inventory, computed crop growth | Proposed |
| [ADR-012](ADR-012-maps-and-warps.md) | Maps and warps: one catalogue, exits as data, the server moves the player | Accepted |
| [ADR-013](ADR-013-economy-coins-escrow-market-trade.md) | Economy: coins, one atomic transfer, shipping bin, consignment market, direct trade | Accepted |
| [ADR-014](ADR-014-coops-and-looks.md) | Co-operatives (membership, shared warehouse) and player looks | Accepted |
| [ADR-015](ADR-015-private-instances-and-placeables.md) | Private per-player farm and farmhouse, Town and Backwoods, player-placed furniture (amends ADR-012) | Accepted |
| [ADR-016](ADR-016-ui-theme-and-hud-layout.md) | One pixel-art UI theme (shared controls, 9-slice frames) and one HUD dock-region layout | Proposed |
| [ADR-016](ADR-016-ui-theme-and-hud-layout.md) | One pixel-art UI theme (shared controls, 9-slice frames) and one HUD dock-region layout | Proposed (partly superseded by ADR-017) |
| [ADR-017](ADR-017-game-menu-and-world-pixel-ui.md) | One game menu, a UI pixel equal to the world pixel, a pixel font (VT323), a near-empty HUD (amends ADR-016) | Accepted |
| [ADR-018](ADR-018-seasons-and-server-calendar.md) | Seasons and the server calendar: two-week seasons, crops by season, clock computed on the client | Accepted |
| [ADR-019](ADR-019-visiting-private-instances.md) | Visiting private instances: allow-list, co-op setting, what a visitor may touch (amends ADR-015 D4) | Accepted |
| [ADR-020](ADR-020-coop-land.md) | Co-op land: a shared farm instance per co-op, entered from Town by membership | Accepted |
| [ADR-021](ADR-021-tools-resource-nodes-ground-items.md) | Farming basics: five tools, resource nodes, ground items with pickup, crop growth in game days (amends ADR-011) | Accepted |
| [ADR-022](ADR-022-chunked-interest-management.md) | Chunks: 16×16 interest management for snapshots and world events, per-chunk state | Accepted |
| [ADR-023](ADR-023-energy-skills-food.md) | Energy per game day, food, farming/foraging/mining skills with XP | Accepted |
| [ADR-024](ADR-024-crafting-and-outdoor-placeables.md) | Crafting recipes, chests, sprinklers, furnace, placeables on the farm (amends ADR-015) | Accepted |
| [ADR-025](ADR-025-crop-roster-and-forage.md) | Crop roster for three seasons, regrowing crops, seasonal forage | Accepted |
| [ADR-026](ADR-026-tool-upgrades-and-the-mine.md) | Tool tiers, blacksmith counter, per-player mine floors | Accepted |
| [ADR-027](ADR-027-weather-and-day-night.md) | Weather from the calendar (rain waters crops), day/night ambient tint and light sources | Accepted |
| [ADR-028](ADR-028-farm-buildings-and-animals.md) | Coops and barns on the farm, private interiors, chickens and cows with daily produce | Accepted |
| [ADR-029](ADR-029-chat-friends-mail.md) | Chat channels with moderation, friends list, mailbox for system messages | Accepted |
| [ADR-030](ADR-030-community-goals-and-market-limits.md) | Seasonal community goals with server-wide rewards; market cornering limit and price history | Accepted |
| [ADR-031](ADR-031-deployment-and-operations.md) | Deployment and operations: images, Caddy TLS, migrations and backups, health, logs, metrics, admin role, load test | Accepted |
| [ADR-032](ADR-032-security-hardening.md) | Security hardening: server key pinning, handler audit tests, input bounds, account recovery, session and abuse limits | Accepted |
| [ADR-033](ADR-033-client-settings-touch-onboarding.md) | Client settings, touch controls, onboarding hints, accessibility | Accepted |
| [ADR-034](ADR-034-market-at-the-trader-counter.md) | The consignment market is a counter in Town: no market tab or key, listing/buying/cancelling need the counter | Accepted |
| [ADR-036](ADR-036-quest-chain-and-requests-board.md) | Tutorial quest chain with coin rewards and the Town requests board (daily delivery requests) | Accepted |
| [ADR-037](ADR-037-mountain-beach-woods.md) | Mountain, Beach and the Secret Woods as public maps: warps, large stumps, shore forage, the Beach bridge unlock (amends ADR-026) | Accepted |
| [ADR-039](ADR-039-audio-hooks.md) | Audio hooks: sound, music and ambience as commands in the frame buffer, Web Audio in `audio.js`, procedurally generated placeholder sounds | Accepted |
