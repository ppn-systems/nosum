# ADR-113: The quest line comes back on every second step of a merchant's talk

## Status

Accepted

Amends [ADR-093](ADR-093-quest-waypoints-quest-lines-and-hint-idle.md) (D3: the quest line is the greeting only). It also refines the conversation
order of [ADR-080](ADR-080-npc-dialogue-lines-and-seasonal-stock.md) (D2). No wire change: `NosumProtocol.Version` (34) stays as it is, and no text is
added; the quest lines already exist in both languages.

## Context

Round three of the NPC and HUD critique (`docs/design/critique-npc-hud.md`, section "Round three") found that the quest's line was said once. The
greeting named the active quest at the merchant it sends the farmer to (ADR-093 D3), but the first Talk replaced it with the rotating pool, and it never
came back in that conversation. A farmer who wanted the step again had to close the box and reopen it. The Talk lines otherwise say nothing about the
quest.

## Decision

- **D1 - The quest line comes back at every second step.** When the active quest's place is this merchant, the quest line is said at the greeting and at
  each second step after it: the steps are numbered from zero, so slots 0, 2, 4 ... say `Npc_{Id}_Quest_{key}`. The steps in between (1, 3, 5 ...) keep
  the rotating pool of ADR-080 D2. A quest line without text falls back to the pool, as before.
- **D2 - Nothing else changes.** The pool, its tags, the day seed and the order of the pool are unchanged. A merchant the quest does not send the farmer
  to has the same lines in the same order as before.
- **D3 - The wire is not touched.** The rule is client-side text selection (ADR-080 D6). The server sends nothing new.

## Alternatives considered

- **The quest line on every Talk.** Rejected: the same sentence twice in a row reads as a stuck merchant, and it crowds out the pool's lines.
- **A progress tag on the pool instead.** Rejected for now: the quest line is one specific sentence about the next purchase, and a tag would need a
  line per quest in the pool, which the pool's sixteen-line limit does not have room for.

## Consequences

- A conversation with the merchant the quest names has the quest in its greeting and in every second step, so the step is never more than two
  presses away.
- Tests: `NpcGreetingTests` (`KeyFor_TheActiveQuestIsAtThisMerchant_TheGreetingNamesIt`, `..._ItComesBackEverySecondStep`).

## Revisit when

- A quest has a place for every step, so the quest line can name the place of the step, not only the merchant.
- Playtest shows the second quest line repeats too often over a session.
