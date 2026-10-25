# ADR-006 — Layered ("paper-doll") character customization

**Status:** Proposed — prototype done 2026-10-25 (one jacket, one hat, three colours).
**Amended by:** [ADR-007](ADR-007-placeholder-content-16px.md) — 16 px grid, content sheets used as shipped, placeholder only.

## Context

Players need many characters and must change clothes and accessories. Pre-rendering every
combination of body, hair, clothes and accessories is impossible. PixelLab produces flattened
sprites only — it cannot export pixel-aligned garment layers
(PixelLab `search_help`, 2026-10-25) — but `create_character_state` re-dresses an existing
character while keeping its pose and canvas, which makes garment extraction possible.

## Decision

- **D1 — Layer stack.** A character is drawn as layers on one 64 × 64 canvas with one feet
  anchor: body (skin, face) → trousers → shoes → top → hair → hat → accessory. Every layer has
  an image per direction and per animation frame, aligned to the base body
  (`assets/characters/villager_*`). Draw order may differ per direction (e.g. a backpack is
  behind the body facing south, in front facing north).
- **D2 — Garments come from the base body.** A garment is made by re-dressing the base body
  with `create_character_state`, then extracting the garment (method recorded in
  `assets/wardrobe/SOURCES.md`) and cleaning stray pixels by hand where needed.
- **D3 — Colour variants are runtime hue shifts**, not new art: one garment image, many colours;
  outline pixels keep their colour. Skin and hair tones use the same mechanism.
- **D4 — Compose once, draw once.** When a character's appearance changes, the client composes
  its layers into one cached image per direction/frame; the frame loop draws that cache, so a
  dressed character costs one draw like any sprite (keeps ADR-002's one-crossing-per-frame rule).
- **D6 — The server decides every look; the wire carries ids only.** The server owns each
  player's avatar parts (body, skin, hair) and equipped items and sends, to every client on the
  map (on join and whenever it changes), only ids: avatar part ids, equipped item ids and dye
  ids. No sprite coordinates, colours or text on the wire. Each client resolves ids to sheets,
  frames, tints and localized names through the shared catalogue. Clients never derive a look
  from a player id or local data; the id-derived look in `RemoteLook.cs` is a stand-in until
  this ships. (Owner decision, 2026-10-25.) Implemented by [ADR-014](ADR-014-coops-and-looks.md): `RemoteLook` is gone.
- **D5 — Appearance is server data.** What a player wears is a list of item ids plus colour
  values, owned and validated by the server (the player must own each item) and sent to players
  on the same map. Wardrobe items are tradable through the market (docs/design/pillars.md).

## Alternatives considered

- **Whole-outfit sprites only** — simplest, but no mix-and-match and every outfit needs every
  animation regenerated.
- **Hand-drawn layers only** — best alignment, far more manual work per item.

## Consequences

- Animations must be fixed on the base body first; every garment then needs the same frames.
- Extraction quality varies per garment; the hat prototype still shows hair at the back edge
  facing north and needs a manual touch-up.
- Each garment costs one PixelLab state (10 generations at 64 px in the prototype).

## Revisit when

PixelLab (or another tool) can export aligned layers directly, or extraction cleanup costs
more than drawing layers by hand.
