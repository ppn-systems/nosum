# HUD, NPC doors, backpack and buttons: captures

Captured with `tests/browser/hud-npc-buttons-capture.cjs` against the Development host on PostgreSQL and a client published with
`-p:NosumDevTools=true` (docs/adr/ADR-079). Each viewport has the same frames: `hud`, `tailor-dialog` (the tailor's box: Buy, Wardrobe,
Backpack, Talk, Leave), `backpack-36`, `backpack-45`, `backpack-54` (the backpack page after each purchase), `inventory-54` (the bag at its
largest size) and `settings-buttons` (the button set on a panel).

Viewports: 1280×720, 1920×1080 and 390×844 (touch).

## Before

The "before" frames of the HUD are the earlier captures in `docs/qa/art-ui-before-*` (before the HUD standard pass). They were not regenerated:
the protocol changed (23 → 24), so the old client cannot talk to the new host.

## Fixed after review (PR #261)

- The touch use button and the emote button are hidden while a merchant's dialog box is open: they covered its right edge at 390×844.
- The chat sender is plain underlined text again, so chat lines keep their height.
- The stray orange tile over Sort in the 390×844 inventory was the hover tooltip of the pointer left resting on that corner by the capture, not a layout fault. The capture now parks the pointer first.
- The phone HUD column already collapses to the Menu button (`GameHud.razor.css`, max-width 640px); the two icons seen at 390 px are the top row and the collapsed minimap button.

## Known gaps

- The three hotbar skill keys sit in one row, so the hotbar is wider than before.
