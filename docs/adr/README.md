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
| [ADR-005](ADR-005-three-quarter-top-down-view.md) | 3/4 top-down view (layers, y-sorting, feet anchors) | Accepted |
