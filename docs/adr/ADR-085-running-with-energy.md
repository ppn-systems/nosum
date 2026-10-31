# ADR-085: Running, a held run speed that costs energy

## Status

Accepted (coordinator, 2026-10-31, owner request). protocol 27 (`MoveIntent.Running`, appended at `SerializeOrder(5)`).

## Context

Walking is the only way to cross the map. The owner asked for a run: hold Shift on a keyboard, or a run toggle on a touch screen, for a faster
farmer that costs a little energy over time. The server decides every position (ADR-003), so the run speed has to be a speed the authority
grants and validates, not a number the client picks. The buff slot work already moves the validator's speed limit with a multiplier
(`StatusEffectRules.MoveSpeedFactor`, issue #146); running reuses that path. Energy is the stored player energy of ADR-023, which the server
already spends through `IPlayerProgress.ChargeEnergyAsync`.

## Decision

- **D1 — Speed.** While running the farmer moves at `RunRules.SpeedFactor` (1.5) times the walking speed, on top of any speed buff
  (`RunRules.SpeedMultiplier(buff, running)` = `buff × 1.5`). The client walker and `MoveValidator` use the same multiplier, so a run the
  client takes is a run the server accepts, and a claim above it is refused as `TooFast` like any other speed hack.
- **D2 — Energy.** Running costs `RunRules.EnergyPerSecond` (0.25): one energy every four real seconds, carried over in fractions
  (`RunRules.TakeEnergy`). The server accrues the cost in the player's presence (`RunEnergyDebt`) and spends whole units in the background
  through `IRunEnergy` (one unit of work, like a fishing cast; the new energy is pushed with `EnergyChanged`). Each call charges at most
  `MaxChargedSeconds` (0.5 s), so standing still and then running is not charged for the idle time. When the energy is gone the server
  withdraws the run speed (`RunAllowed = false`) and grants it again once the client lets go of run. The client stops claiming run at zero
  energy (it reads `ProgressChannel.Energy`), so the withdrawal is seldom seen.
- **D3 — Input.** `InputState.Run` (16) is a held bit above the four directions; `InputPacking.HeldBits` becomes 5 and the edges move up
  with it. Shift (either side) sets it in `renderer.js`; on touch a latched Run button (`data-touch-run`) toggles it in the page script, so
  the frame loop reads one integer as before. The client claims running only while it walks with run held and has energy.
- **D4 — Animation.** The walk animation runs `1.6` times faster while running (`MapScene`), so the farmer's stride looks quicker. The
  speed is not raised for the animation; only the step length is.
- **D5 — Minimap setting (Settings > Display).** The minimap card can be hidden, remembered per player (`ClientSettings.MinimapHidden`,
  stored as `minimap=auto|0|1`). `auto` (the default) hides it only on a screen narrower than 640 px, so a phone starts without it.

## Consequences

- The wire changes (`MoveIntent` gains a field): `NosumProtocol.Version` 24, one client and one server ship together.
- A failed energy write costs that run step's energy, not the player's run; the next run step owes energy again.
- The run speed and energy rules have no client-side store: a reconnect starts with the server's `RunAllowed = true`.
- Running is not a buff: it does not show in the buff slot and does not stack with a potion's effect beyond the product of the two factors.
- Tests: `RunMovementTests` (the validator accepts a run at 1.5× and refuses walking speed for a run and a 2× pace), `RunInputPackingTests`,
  and the JavaScript packing constants in `InputPackingTests`. The server path (`PlayerService`) is covered by the validator and rules tests;
  it has no end-to-end test.

## Alternatives considered

- **Client-decided speed with a looser server limit.** Rejected: the server would have to accept any run, which is a speed hack.
- **Energy spent on every move packet.** Rejected: about ten database writes a second per player. Fractions accrue in memory and whole units
  are spent in the background.
- **A run speed that stacks with the buff additively.** Rejected: the product matches the buff's own multiplier and keeps one rule.

## Revisit when

- Running needs a stamina bar on the HUD or a cooldown; the energy bar is the only cue for now.
- Energy regeneration or food gives a run bonus.
