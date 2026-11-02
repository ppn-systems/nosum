# Round three: the quest line on a merchant's talk

Captured by [`tests/browser/npc-talk-quest-capture.cjs`](../../../tests/browser/npc-talk-quest-capture.cjs) on the Development host (PostgreSQL,
`NOSUM_CALENDAR_EPOCH` set to about 11:00 on day 1) with a client published with `-p:NosumDevTools=true`. The quest stage is buy seeds (the
shortcut of `npc-hud-round3-capture.cjs`). Review: [`docs/design/critique-npc-hud.md`](../../design/critique-npc-hud.md), "Round three". Decision: [ADR-113](../../adr/ADR-113-quest-line-returns-on-talk.md).

| Frame | Step |
| :-- | :-- |
| `talk-greeting-<lang>-<WxH>.png` | the greeting: the buy-seeds quest line |
| `talk-1-<lang>-<WxH>.png` | the first Talk: a pool line |
| `talk-2-<lang>-<WxH>.png` | the second Talk: the buy-seeds quest line again |

`talk-<WxH>.json` records, for each step and language, whether the quest line was in the box, and the console and page errors (none).
