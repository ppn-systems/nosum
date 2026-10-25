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
| [ADR-004](ADR-004-pixel-art-assets.md) | Pixel-art assets: 32 px grid, source art in `assets/`, atlases built by a repo tool | Proposed |
| [ADR-006](ADR-006-layered-character-customization.md) | Layered (paper-doll) character customization | Proposed |
| [ADR-005](ADR-005-three-quarter-top-down-view.md) | 3/4 top-down view (layers, y-sorting, feet anchors) | Accepted |
