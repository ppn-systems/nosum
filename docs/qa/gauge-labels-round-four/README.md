# Round four: the gauges name their tubes

Captured on a Development build (PostgreSQL, a client published with `-p:NosumDevTools=true`), by
[`tests/browser/rebuild-npc-hud-capture.cjs`](../../../tests/browser/rebuild-npc-hud-capture.cjs), one run per size. Review:
[`docs/design/critique-npc-hud.md`](../../design/critique-npc-hud.md), "Round four".

| Frame | What it shows |
| :-- | :-- |
| `hud-farm-<lang>-<WxH>.png` | the farm HUD on a normal day: the hotbar, and the HP, MP and Energy tubes beside it, each with its name under it |

The tour logs (`tour-<WxH>.json`) list the other frames each run took (journal, merchant) and the console errors: none at any size.

**Before:** the hotbar gauges had no names on screen (tooltip only). The before frames are the round-one captures in
[`docs/qa/rebuild-npc-hud/before/`](../rebuild-npc-hud/before/) (`hud-farm-<lang>-<WxH>.png`). They were not re-taken on
master for this round; the difference to look for is the caption row under the tubes.
