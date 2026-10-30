# ADR-074: Tool action pacing and feel

## Status

Accepted

## Context

Every farm action felt the same: a tool was used the moment its key or mouse button asked, the farmer snapped into a pose for a frame, and a drag over a
field worked tile after tile with nothing to see or hear per tile. Three gaps caused it.

- **The mouse path had no swing.** Drag-to-sow (docs/adr/ADR-021, #201) sent a farm use per tile every 0.12 s from `PlayerActions.DragSow`. It did not
  start a swing, play a sound, or lock the farmer, so a hoe or watering can dragged across a field looked like it did nothing.
- **The server paced tools only.** `FarmService.UseCoreAsync` took the 300 ms swing cooldown (`ToolStateService`, docs/adr/ADR-021 D8) only when the held
  item was a tool. Seeds, harvest and bare-hand uses had no interval, only the handler's rate limit of eight per three seconds (`farm-use`).
- **The key path had no rest.** The key path started a 300 ms swing and allowed the next use after 320 ms, with the server use sent at press. Nothing
  held the farmer during the swing, and the hit was not separated from the wind-up.

The brief also asks for impact feedback (dirt, water, wood, sparks), a cost label, and an exhausted state at low energy, all with reduced motion respected.
The engine stays platform-free, the frame keeps one interop crossing (docs/adr/ADR-002), and the wire stays append-only (docs/adr/nalix-packets).

## Decision

- **D1 — A swing is the action, and the hit is the request.** A use starts a swing whose length depends on the tool (`ToolPacing.SwingMs`: hoe 420 ms, watering
  can 460 ms, axe 440 ms, pickaxe 420 ms, scythe 400 ms, sword 360 ms, staff 400 ms, seeds and bare hands 380 ms). The farmer is locked in place until the
  swing ends (`PlayerActions.SwingLocked`, `MapScene.Frame`). The request for the use is queued and sent at the hit frame (`ToolPacing.HitFraction`, 45 % of the
  swing), so the wind-up is visible before anything changes on the server. The wind-up, the strike and the settle are the existing cleave curve, now
  split at the hit frame.
- **D2 — The server paces every farm action, authoritatively.** `FarmService.UseCoreAsync` begins an interval for every non-tail use, whatever is held:
  a tool, a seed, a harvest or empty hands. A use less than `ToolPacing.MinIntervalMs` (300 ms, `NodeLimits.SwingCooldownMs`) after the previous one is
  refused with the existing `FarmStatus.Cooldown` code and changes nothing. `ToolPacing.IsTooSoon` is the one rule; `ToolStateService` uses it. The
  area tail of a wide use is worked under its first tile's interval, as before.
- **D3 — Dragging repeats at the animation rate.** The drag takes the next tile only when the farmer is free again: the previous swing and its recovery
  (`ToolPacing.RecoveryMs`, 80 ms) are over. The client never sends sooner than the server's interval (every swing is at least 380 ms plus recovery), and
  the spacing of two sent uses stays inside the handler's rate limit of eight per three seconds. A drag that lags behind the pointer works the tile under
  the pointer when the farmer is free, so no tile is skipped silently and none is worked twice.
- **D4 — Exhaustion and cost.** At or below a quarter of the maximum energy (`ToolPacing.IsExhausted`) every swing runs 35 % slower (`SwingMs(tool, true)`) and
  a sweat bubble is drawn above the farmer (`FarmerSprite.DrawSweatBubble`). The energy a successful tool use spent floats above its tile as "-N" (a
  `CombatLabels` number, the server's answer `FarmStatus.Ok` with an outcome that charges energy: till, water, hit, break, clear, cut). The cost is the
  server's `EnergyRules.Cost` at the player's skill level, computed on the client from the progress the server pushes. A wide use shows no label, because
  the reply does not say how many tiles were charged.
- **D5 — Impact feedback.** At the hit, farm tools and seeds throw five flecks from the tile (`FarmerSprite.DrawToolHitParticles`): dirt for the hoe, droplets
  for the watering can, chips for the axe, sparks for the pickaxe, leaves for the scythe, soil for seeds. The sound plays at the hit as well, through the
  existing `AudioCatalog.ToolSound`, instead of at the key press. Reduced motion (`ViewOptions.ReduceMotion`) keeps the swing and the sound and drops the flecks and
  the rise of the energy label.
- **D6 — What does not change.** Combat strikes (sword, staff, skills) keep their immediate request: combat has its own timing rules (docs/adr/ADR-055 to ADR-064),
  and only their swing animation and the lock follow this ADR. No wire field, opcode or status code is added: `FarmStatus.Cooldown` already exists, so
  `NosumProtocol.Version` stays at 12. No new art is drawn: the farmer's action column, the tool sheets and the existing impact overlays are reused, so no
  asset provenance changes (docs/adr/ADR-040). No display text is added: the cost is a number and the bubble is a shape, so the i18n files are unchanged.

## Consequences

- A farm action is paced by the server, so a client that mashes keys or scripts the protocol gets `Cooldown` refusals, not a faster farm.
- The interval now also applies to seeds and harvests. A player who plants a field by drag takes about 0.46 s per tile instead of 0.12 s; the drag is
  deliberately slower, as the brief asks.
- The energy label's prediction is the server's own rule, but a player whose skill changes mid-swing may see the label one number off for that swing.
- Drag-to-sow still covers the farm items only (seeds, hoe, watering can), as #201 defined it. Axes and pickaxes are used with the action key.

## Tests

- `ToolPacingTests` (unit): every tool's swing is no shorter than the server interval, the farm swing fits the handler's rate limit, the exhausted swing is
  slower, the hit frame falls inside the swing, the interval rule matches the server boundaries (299 ms refused, 300 ms allowed), and exhaustion starts at a
  quarter of the energy.
- `ToolStateServiceTests` (unit): the existing cooldown tests use the same rule.
- `FarmWireTests.Farming_UseSentSoonerThanTheInterval_IsRefusedWithCooldownAndChangesNothing` (integration): a use sent inside the interval is refused with
  `FarmStatus.Cooldown`, and the tile is untouched, which the next use after the interval proves.
