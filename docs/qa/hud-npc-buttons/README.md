# HUD, NPC doors, backpack and buttons: captures

Captured with `tests/browser/hud-npc-buttons-capture.cjs` against the Development host on PostgreSQL and a client published with
`-p:NosumDevTools=true` (docs/adr/ADR-079). Each viewport has the same frames: `hud`, `tailor-dialog` (the tailor's box: Buy, Wardrobe,
Backpack, Talk, Leave), `backpack-36`, `backpack-45`, `backpack-54` (the backpack page after each purchase), `inventory-54` (the bag at its
largest size) and `settings-buttons` (the button set on a panel).

Viewports: 1280×720, 1920×1080 and 390×844 (touch).

## Before

The "before" frames of the HUD are the earlier captures in `docs/qa/art-ui-before-*` (before the HUD standard pass). They were not regenerated:
the protocol changed (23 → 24), so the old client cannot talk to the new host.

## Known gaps seen in these captures

- At 390×844 the HUD still shows more than the single Menu button that ui-standard section 6 asks for. This predates this change.
- At 390×844 the touch action button overlaps the right edge of the merchant dialog box.
- At 390×844 a stray tab icon overlaps the Sort button in the inventory.
- The chat sender names are now stone buttons (ui-standard section 8); the chat lines are taller as a result.
- The three hotbar skill keys are laid out in one row; the hotbar is wider than before.
