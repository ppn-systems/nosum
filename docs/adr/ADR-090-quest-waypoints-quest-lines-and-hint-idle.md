# ADR-090: Quest waypoints, the merchant's quest greeting, and hints that wait for an idle moment

## Status

Accepted

Amends [ADR-033](ADR-033-client-settings-touch-onboarding.md) (the first-run hints, D3), [ADR-080](ADR-080-npc-dialogue-lines-and-seasonal-stock.md) (the greeting, D2) and
[ADR-036](ADR-036-quest-chain-and-requests-board.md) (the quest chain). No wire change: `NosumProtocol.Version` does not move.

## Context

The critique of the NPC, quest and HUD round ([docs/design/critique-npc-hud.md](../design/critique-npc-hud.md)) left five open items: the quest had no
place on the map (finding 1), the merchants never named the quest (finding 2), the tailor's buttons were jargon (finding 3), a name plate covered an exit's
direction label (finding 5), and a hint came up the moment its step changed, over the action the player was in (finding 8). This ADR decides how each one
is solved and what stays on the client.

## Decision

- **D1 — A quest's place is data, by key.** `QuestWaypoints` (gameplay) maps a quest's key to a place key. A place is a merchant (its tile is read from
  `NpcCatalog`, so moving a counter moves its waypoint) or a map (its spawn tile, for the Forest and the Mine). Eight quests have a place (the seed shop,
  the blacksmith, the carpenter twice, the furnisher, the Town board, the Forest and the Mine); the rest have none and show no waypoint. The table is read
  by the client only; the server does not use it.
- **D2 — The waypoint is drawn by the engine.** The active quest is the first active quest in chain order (`ActiveQuest`, the same rule the tracker uses).
  When its place is on the current map and off the screen, `EdgeArrow` (engine) draws an arrow at the view's edge, kept 24 world pixels from the edge
  and pointing along the ray from the centre to the place; the arrow is a gold shape with a shadow, drawn with rectangles only. On the minimap the place
  is a green dot. A waypoint on another map shows nothing (the map's own exits already show the way out).
- **D3 — The greeting names the active quest, once.** When the active quest's place is this merchant, the merchant's *greeting* (slot 0) is the line
  `Npc_{Id}_Quest_{quest key}`, in both languages. Talk goes on through the rotating pool as ADR-080 D2 describes. A quest line without text falls back to the
  pool, so an untranslated key never shows a raw key.
- **D4 — A name plate lifts above an exit's label.** The exit's direction label sits over its tile (`WayfindingDecorator`), so a farmer within 40 world pixels
  of an exit has the label over the head. The name plate is lifted by 16 pixels there and only there. The label's height is not known to the client, so 16
  is the label's band, not a measured box; a longer label would still need a new rule.
- **D5 — A hint waits for the player to be idle.** A new hint (a new step, a new quest step, a settings change that shows one) is requested, not shown:
  it comes up after `IdleSeconds` (2) with no input to the game, and each input restarts that count. Input is `GameLoop.PlayerActivity`, raised from the frame
  whenever a key or touch is held or pressed. A hint on screen is not disturbed by a settings change that leaves its step the same.
- **D6 — The tailor's labels say what they do.** "Wardrobe" becomes "Change clothes" and "Backpack" becomes "More bag space" (Vietnamese: "Thay đồ" and
  "Mở rộng túi"). Keys are unchanged.

## Alternatives considered

- **A waypoint arrow from the server.** Rejected: a target is a client view of quest data and text the server never sends (ADR-080 D6).
- **A waypoint for every quest.** Rejected for now: tilling, watering, cooking and fishing have no fixed tile in the data; inventing one would send the player
  to a wrong place. The table grows as places are authored.
- **Measure the label and stack the plate exactly.** Rejected for now: the client has no label size, and a fixed lift is enough at the exits that exist.

## Consequences

- The Forest and the Mine waypoints point at the map's spawn tile, which is where a player arrives, not the nodes they will work. A later change may add a
  node tile per map.
- The flaky `FootwearSpeedWireTests.WithWingedBoots_ARunAboveTheCapIsRefused` (and its siblings) now decide refusals with a single claim beyond any budget, and
  accepted walks wait at least one step between claims. The server still reads the real clock for movement (`PlayerService` takes `TimeProvider.System`, as
  the swing cooldown and the snapshot loop do); the tests no longer depend on how long a claim took.
- The onboarding timing test (`SettingsTests`) uses a timer clock that the test moves by hand.

## Tests

- `QuestWaypointsTests`: every quest and place named in the table exists, the waypoints are on their maps, a quest with no place has none.
- `EdgeArrowTests`: the arrow sits on the edge the ray leaves through, points that way, stays inside the view, and nothing is placed for a target on screen.
- `QuestWaypointViewModelTests`: the waypoint follows the active quest in chain order and raises `Changed` once when it moves.
- `NpcGreetingTests`: the greeting names the quest at its merchant only; the Talk lines and other merchants are unchanged; a missing line falls back.
- `SettingsTests` (`Hints_WaitForAnIdleMoment_AndFadeAfterTheirTime`, `Hints_NextHintComesAfterTheStepIsDoneAndTheIdleMoment`): the idle wait, its restart on input,
  and the fade.

## Revisit when

- Tilling, watering or a monster quest has a place on a map.
- A label's size is known to the client, so the name plate stacks on its real box.
- The server keeps a visit record, so a merchant can greet a farmer who has never spoken to it before.
