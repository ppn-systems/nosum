# ADR-027 — Weather and day/night lighting

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M3. Builds on ADR-018 (calendar), ADR-021 D7
(growth in watered days), ADR-022 (chunks), ADR-002 (one render crossing per frame).

## Context

Every game day looks the same and every crop must be watered by hand. Weather gives days a character, makes rain a
reward, and the calendar already gives every player the same day.

## Decision

- **D1 — Weather is a pure function of (calendar epoch, day index)** using a seeded hash: sun, rain, storm (summer
  only), snow (winter only). Probabilities per season are data (spring rain 20 %, summer 12 % rain + 5 % storm, fall
  20 %, winter 25 % snow). The first two days of a season are always sunny. No stored state; the client computes the
  same weather from the calendar info it already has, and the server's rule is authoritative for gameplay.
- **D2 — Rain waters** every outdoor tilled tile on every instance for that day (the growth rule treats a rainy day as
  watered; no writes needed). Indoor maps are unaffected. Storms may strike: out of scope until lightning rods exist.
- **D3 — Tomorrow's forecast** is shown on the HUD calendar widget (icon), computed by the same function for day + 1.
- **D4 — Day/night lighting.** The engine gets one ambient tint per frame (a `SetAmbient(r, g, b, a)` render command
  in the existing command buffer, applied by `renderer.js` as one full-canvas composite before the UI): dawn 06:00,
  day, dusk from 18:00, night from 20:00 to the day boundary, interpolated. Indoor maps are lit (no tint) except at
  night (mild tint). Weather darkens the tint slightly when raining.
- **D5 — Light sources.** Placeables with `LightRadius` (lamps, torches, campfire) cut light holes in the night tint:
  the client draws them as additive radial sprites in the same pass, at most 64 per frame, culled to the camera.
- **D6 — Rain/snow particles** on the client only: a cheap screen-space particle layer (fixed pool, no allocation),
  disabled indoors.
- **D7 — The day rollover stays the calendar's day boundary (ADR-018).** Night lighting runs from 20:00 to the boundary;
  players are never forced to sleep (no fainting); energy refills at the boundary (ADR-023).
- **D8 (amendment, #134) — Rain bonus on harvest.** An outdoor crop harvested on a rainy day (rain or storm), or on the
  day after rain, may yield one more unit. The roll is a deterministic SplitMix64 over the map instance key, the tile
  and the harvest day, salted apart from the revert roll; 20 percent of such harvests get the bonus. The server adds it
  in `FarmRules.IsBonusHarvest` and the drop, the audit and the reply all count the same units. Indoor tiles never get
  it. No new item ids (no separate "quality" crop), no stored field, and no wire change: the reply already carries the
  quantity. A separate quality item with a higher sell value was the larger alternative and is not built; it can
  replace this later with its own item ids (ADR-011 stable ids) if the design calls for it.

## Consequences

One new render command (append-only in the command-buffer contract, both sides updated together). No new wire
packets: weather derives from `CalendarInfo`. The rain bonus adds no packets and no stored state; the server rule is
authoritative and the client never computes it.

## Amendments

### 2026-11-01 — weather at dusk and night; storm streaks (rebuild-world-ambience)

- **D4 (amended).** Rain and storm opacity is added to the tint **in proportion to the light left**: `extra × (255 − A) / 255`, where `A` is the
  day's tint opacity. At noon the weather adds its full amount, as before; at night it adds about a third of it, so a rainy night is a little
  darker than a clear one without stacking onto the night tint (196 of 255 before, 177 now). Indoors and the daytime tint are unchanged.
- **D6 (amended).** A storm's drops are drawn as two segments, the lower one a pixel to the left, so they lean with the wind. Rain is unchanged
  (one segment). No new draw kind; a storm frame holds at most three draws per drop (two segments and a splash).
- Evidence and frames: `docs/design/critique-world-ambience.md`, `docs/qa/rebuild-world-ambience/`.

### 2026-11-01 — snow flakes and fall leaves (world-ambience round 2)

- **D6 (amended again).** A **near** snowflake (depth above one half) is drawn two pixels square; a far one stays one pixel. On the round-one
  winter frame the one-pixel flakes were lost against the grass and the path (`docs/qa/rebuild-world-ambience/before/en/1280x720/snow-winter-snow-farm.png`).
  The snow still draws one rectangle per flake, so the draw count is unchanged. Test: `SkyRenderingTests.Precipitation_Snow_NearFlakesAreTwoPixelsSquareAndFarOnesOne`.
- **Autumn leaves** (sun in fall, D6) were graded in round 2 and not changed: small orange, red and gold squares read as leaves on the grass.
- Evidence: [docs/qa/world-ambience-round2/](../qa/world-ambience-round2/README.md).

