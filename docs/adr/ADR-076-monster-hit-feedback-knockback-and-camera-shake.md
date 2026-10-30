# ADR-072: Monster hit feedback: white flash, knockback, hit-stop and camera shake

## Status

Accepted

## Context

A landed hit on a monster plays the `monster_hit` sound and a floating number (ADR-055), and the HP bar appears once the monster is damaged. The
sprite itself does not react: no flash, no knockback, no pause and no shake, so strikes feel weightless even when they hit hard (issue #212).

The constraints are the project's. The server decides every outcome (docs/adr/ADR-003, the gameplay rules). The wire format is append-only
(docs/adr/ADR-009 and the `nalix-packets` rules). The engine stays platform-free and the frame keeps one interop crossing (docs/adr/ADR-002). Reduced
motion (docs/adr/ADR-033) turns off shake and pauses. The player's own swing animation belongs to another change and stays as it is.

## Decision

- **D1 — The knockback is server-authoritative and uses the existing monster position.** On a landed hit that does not kill, the server pushes an
  ordinary monster one tile directly away from the attacker, when the grid allows the step and no other living monster holds the tile. The rule is
  `MonsterStepRules.TryKnockback` in `Nosum.Gameplay`, next to the monster step rules it reuses (`CanStep`, `IsOccupied`). The new tile is written to
  the instance and sent in the `MonsterChanged` push that already carries `X` and `Y`. No packet or field is added, so `NosumProtocol.Version`
  stays at 14.
- **D2 — Bosses are not pushed.** `CombatRules.IsBoss` monsters stand their ground. A knockback that is blocked leaves the monster where it is; the
  hit still counts.
- **D3 — The client reads the hit and the move; nothing is stored on the server for feel.** `CombatChannel` records the last landed hit on a monster
  (its tick and impact) and the tile a monster last left. The impact is `Light`, `Heavy` (the crit-styled damage number, the same rule as
  ADR-055's style 1) or `Finisher` (the combo finisher of ADR-060). Its damage number and its shake read the same impact, so they stay in step.
- **D4 — The sprite reacts for about 90 ms.** The white flash is a render command flag, `RenderCommandBuffer.Whiten`, that washes the sprite toward
  white over its own silhouette by the alpha byte of its tint. Heavy hits and finishers flash brighter. The monster is drawn at its old tile and
  slides to the new one over 100 ms (a move from any cause is shown the same way).
- **D5 — Hit-stop holds the sprite's pose for 60 ms.** The struck monster's sway and frame clock freeze at the moment of the hit. It is a
  monster-side hold only: the world, the player's swing and the rest of the frame keep running.
- **D6 — A crit or a finisher shakes the view.** Heavy hits shake 2 pixels for 140 ms; finishers 3 pixels for 220 ms. `CameraShake` in
  `Nosum.Rendering` is a pure, allocation-free function of the elapsed time, and `Camera2D.Nudge` applies it after the camera follows the player.
- **D7 — Reduced motion keeps only the flash.** With `ViewOptions.ReduceMotion`, the slide snaps, the hit-stop and the shake are skipped. The flash
  stays, because it changes colour and does not move anything.

## Consequences

- A monster hit from contact reach is pushed out of reach of its counter-attack (`CombatRules.MonsterStrikesBack`), so a melee fight loses some
  counter-damage. This is intended: the push is part of the feel the issue asks for.
- Every tile move the server broadcasts now also slides on the client, including the wandering of idle monsters. The slide is 100 ms and only
  smooths a step that already happened.
- A knockback does not check for players standing on the pushed tile, and neither does the monster step rule it reuses: `IsOccupied` only sees
  other monsters. A monster can end up on a tile a player stands on. A follow-up can add a player check if it shows up in play.
- The whitening is a second path through `renderer.js` that cannot be hit by existing draws: only sprites with `Whiten` set take it, and it caches
  one canvas per alpha value per sheet, so the flash costs one canvas per step of the fade, not one per frame.
