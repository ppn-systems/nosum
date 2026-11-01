# ADR-095: The quest waypoint arrow stays off the HUD, the waypoints point at the nodes, and the hint's idle count costs nothing per frame

## Status

Accepted

Amends [ADR-093](ADR-093-quest-waypoints-quest-lines-and-hint-idle.md) D1 (the Forest and Mine places), D2 (the edge arrow), D4 (the name plate, which is kept, now
measured) and D5 (the idle count). No wire change: `NosumProtocol.Version` does not move.

## Context

ADR-093 shipped without browser captures. The round 3 check ran the shipped build in Chromium through the Development QA hook (`window.nosumQa`, ADR-079), at
1280x720, 1920x1080 and 390x844, in Vietnamese and English (`tests/browser/npc-hud-round3-capture.cjs`, captures in
[`docs/qa/npc-hud-round3/`](../qa/npc-hud-round3/README.md)). It found:

- **The edge arrow sat under the HUD.** At 1280x720 the arrow for the seed keeper, from Town, was drawn over the energy gauge and the hotbar dock. The arrow for the
  tree in the Forest was drawn over the calendar bar. The arrow is canvas pixels and the HUD is DOM over it, so nothing hid it from the engine.
- **The Forest and Mine waypoints pointed at the spawn tiles**, which is where a player arrives, not what they chop or mine (ADR-093 consequence, now fixed).
- **The HUD rectangles the renderer reads left out the top-right column at 1920x1080.** `refreshHud` skipped any region at least 90% of the canvas height, and the
  top-right column (calendar, minimap, menu) is taller than that there, so world labels and the arrow ignored it. The filter now skips only regions that cover most
  of both dimensions.
- **The hint's idle count re-armed a timer every frame** while the player held a key (`NoteActivity` called `ArmIdle` per frame). Each `CreateTimer` allocates.
- **The name plate lift is a fixed 16 world pixels**, and a world pixel is `scale` screen pixels. The exit label is about 18 screen pixels tall. The lift clears it at
  every zoom the game chooses (scale 2 on the phone, 3 at 1280x720, 5 at 1920x1080: 32, 48 and 80 screen pixels), and the floor of the zoom is 2, so it was not changed.

The greeting was correct in both languages at all three sizes, and the minimap dot showed at the two desktop sizes.

## Decision

- **D1 — Waypoints at the nodes (amends ADR-093 D1).** The Forest's place is the tree at (65, 9) in `Forest.tmx` (the `Paths` layer, tile 11), the nearest tree to
  the spawn tile (68, 0). The Mine's place is `MineWorld.CaveShaft` (23, 9), the cave shaft down to the ore. The copper ore lies on the floors below, which are
  generated per player and per day, so no fixed ore tile exists to point at. The table stays data read by the client only.
- **D2 — The edge arrow avoids the HUD (amends ADR-093 D2).** The engine still places the arrow where the ray from the view's centre leaves the inset view
  (`EdgeArrow.TryPlace`), and records it as two commands of the new texture `EdgeArrowTexture` (-12), the shadow first. The renderer knows the HUD: it reads the
  six HUD regions' rectangles every 250 ms for world labels (`refreshHud`). When the arrow's box overlaps one, the renderer slides it along its edge (up and down
  arrows along x, left and right along y), one view pixel at a time, nearest first, until it is clear. When no place on that edge is clear the arrow is not drawn.
  The glyph is drawn in the renderer from the same row table as `EdgeArrow`'s former rectangles (one `fillRect` a row, the head widening by two cells). No
  allocation in the frame, and the arrow still costs no extra interop crossing: the commands are part of the frame's one submit.
- **D3 — The idle count costs nothing per frame (amends ADR-093 D5).** `NoteActivity` (called on each frame that has input) only records the time of the last input
  (`TimeProvider.GetTimestamp`). The idle timer, armed when a hint is requested, fires after `IdleSeconds`; if input came since, it re-arms for the rest of the quiet
  time instead of showing the hint. A hint comes up exactly `IdleSeconds` after the player's last input.
- **D4 — The name plate lift stays at 16 world pixels (ADR-093 D4 kept).** The captures measure the clearance at scales 2, 3 and 5: the plate sits above the exit
  label with a gap at each. A measured label box is not added: the lift is in world pixels and the label in screen pixels, so a measured box would need the zoom
  as well, and the zoom never goes below 2 (`MapScene.ScaleFor`).
- **D5 — Tests.** `QuestWaypointsTests` pins the Forest tree and the Mine shaft (not the spawn tiles). `SettingsTests` gives its test clock simulated timestamps, so
  the idle count is exact. The browser check (`npc-hud-round3-capture.cjs`) reads the arrow's pixels and the HUD rectangles from the DOM, and the minimap dot.

## Alternatives considered

- **Keep the arrow in the engine and pass it the HUD rectangles from .NET.** Rejected: the rectangles are DOM, and the renderer already reads them. Passing them
  down would be a second source of HUD geometry.
- **Hide the arrow whenever it overlaps the HUD.** Rejected: at the edges the HUD covers most of the view, so the player would often see no arrow at all.
- **Measure the label's box and stack the plate exactly.** Rejected for now (see D4). The fixed band clears at every zoom the game uses.

## Consequences

- The arrow may sit away from the exact edge point of the ray when the HUD covers it: it slides along the nearest clear part of that edge. The direction it points
  is unchanged.
- The `.emote-layer` is a full-viewport transparent overlay, so a bounding-box check reports it on every capture. The arrow's own check ignores it in the reading.
- Idle allocation is about 17 KB a second in the Development build, with or without input, so the frame still allocates. The check did not isolate its cause;
  the hint's per-frame timer is gone, and held input allocates about what it did before (about 22 KB a second, before and after). Finding the rest is a separate
  piece of work.
  Reducing the frame's allocations is a separate piece of work.
- The minimap is not on screen at 390x844 (the phone build has no minimap card), so the minimap dot is checked at the two desktop sizes.
- The bus stop's direction label (`→ Trạm xe buýt`) is drawn over the farmer's head beside the farmhouse door (critique finding 5). It is world-anchored and is not a
  name plate; the lift does not cover it. It stays open.

## Tests

- `QuestWaypointsTests`: the chop-tree waypoint is the Forest tree at (65, 9), not the spawn tile; the mine-ore waypoint is the cave shaft.
- `SettingsTests` (`Hints_WaitForAnIdleMoment_AndFadeAfterTheirTime`, `Hints_NextHintComesAfterTheStepIsDoneAndTheIdleMoment`,
  `Hints_FollowTheStepsInOrderAndAreShownOnce`, `Hints_TurnedOffOrFinished_ShowNothing`): the idle count restarts on input and counts from the last input.
- `EdgeArrowTests`: the placement is unchanged; the HUD-avoiding slide is in the renderer and is checked by the browser capture.
- Browser: `tests/browser/npc-hud-round3-capture.cjs` (grades in [`docs/qa/npc-hud-round3/README.md`](../qa/npc-hud-round3/README.md)).

## Revisit when

- A place for ore exists on a map the player can walk to (a fixed node for the copper), so the Mine's waypoint points at it.
- The game gets a zoom of 1, so the name plate lift needs the label's measured height.
- The frame allocates nothing in idle play, so the allocation check can assert zero.
