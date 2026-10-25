# ADR-033 — Client settings, touch controls and onboarding

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M5. Builds on ADR-017 (tidy UI, one game
menu), ADR-002 (input batching into the frame), ADR-010 (i18n).

## Context

The game is keyboard-only, has no settings beyond the language, and teaches the player through one quest. Many browser
players are on phones and tablets.

## Decision

- **D1 — Settings page** (game menu tab): UI scale (auto / 2–6 world pixels), music and sound volume (for M6 audio),
  key bindings (rebind the action keys; stored per account on the server as a small JSON blob), show names over heads,
  reduce motion (no screen shake, no particles). Per-device settings (volume, scale) in browser storage; account
  settings on the server.
- **D2 — Touch controls.** On a coarse pointer: a virtual joystick bottom-left, an action button bottom-right, the
  hotbar tappable, long-press on a tile to use the held item there; inputs are packed into the same per-frame input
  batch as keys (one crossing per frame). The HUD layout adapts at narrow widths (portrait: hotbar 2 rows of 5).
- **D3 — Onboarding.** A short tutorial quest chain after the first harvest (clear debris, sell at the bin, buy at a
  shop, visit Town) with one-line contextual hints that fade, never blocking panels; skippable in settings.
- **D4 — Accessibility.** All interactive controls reachable by keyboard and labelled for screen readers; contrast of
  text on parchment ≥ 4.5:1; no information by colour alone.

## Consequences

Input packing gains touch fields (append-only in the input contract); settings add one account column.
