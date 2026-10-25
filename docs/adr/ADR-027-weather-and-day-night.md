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

## Consequences

One new render command (append-only in the command-buffer contract, both sides updated together). No new wire
packets: weather derives from `CalendarInfo`.
