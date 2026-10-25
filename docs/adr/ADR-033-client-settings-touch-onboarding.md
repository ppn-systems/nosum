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

## Implementation notes

Where the ADR was silent or the owner's brief narrowed it, the simplest thing was chosen:

- **All settings are per browser.** Key bindings are stored in local storage with the other settings (one `nosum.settings.v1` entry, `key=value` lines), not on the account: no account column, no packet, no wire change. Moving them to the account later is additive. Local storage access is wrapped in the page script; blocked storage reads as "nothing stored" and drops writes, so the settings then last for the visit.
- **Language** is the account's language as before; the browser also remembers the last one used, so the sign-in screen starts in it.
- **Zoom** (auto or 2-6) overrides the engine's world zoom and the UI pixel together; the UI pixel is still lowered when the menu would not fit the height, and a room is never zoomed past showing it whole.
- **Reduce motion** drops weather particles and runs CSS animations for one frame; the system preference does the same. **High contrast** darkens every text colour. All text colours are at least 4.5:1 on the parchment (a unit test checks the theme file).
- **Rebindable:** the four walk keys, use, and the panel and furniture keys. The arrows, Space, number keys, Escape, Enter and Tab are fixed. Binding a key another action holds swaps the two.
- **Touch:** stick and use button are plain elements; `renderer.js` turns their pointer events into the held and pressed bits of the next frame (no .NET call per touch event). They show only where the main pointer is coarse and hide while a panel is open. The hotbar is tappable as before. Long-press on the world fires the use action on the tile in front of the farmer, not on the tile under the finger.
- **Hints** are client-side, not server quests: walk, use, sell, open a shop, visit Town, one line at a time for 12 seconds; finished steps are stored with the settings (shown once; off or restart in the settings). The server quest chain of D3 is not built.
- **Sound** volumes are stored and shown; there is no audio before M6.
- **Not done:** touch buttons for rotate/grab/pick-up (furniture arranging needs a keyboard), per-tile long-press targeting.
