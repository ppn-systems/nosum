# ADR-097: Monster strike telegraph and touch prompts

## Status

Accepted (2026-11-01). Amends ADR-046 (monster contact attacks) and ADR-047 (the boss telegraph, which is unchanged here). protocol 32: one new
push (`StrikeTelegraph`, S2C `0xC249`), no existing packet or field changed. No `DbBudget` row changes: the strike writes nothing to the database
beyond the damage that already applied. Evidence: [docs/qa/combat-round3/](../qa/combat-round3/README.md).

## Context

An ordinary monster struck on the same AI tick it came into reach. The farmer saw no warning: the number landed on the frame the monster arrived
(`docs/design/critique-combat-fishing.md`, item 4). Only the boss ground slam telegraphed (ADR-047). A fight against ordinary monsters was a race
of hit points, and a player could not learn the timing of a strike, so could not step away from one.

## Decision

**D1. An ordinary monster winds up its strike.** When a non-boss monster is adjacent to its nearest player on an AI tick, and it has no strike
already in its wind-up, it starts one. The wind-up lasts `CombatRules.MonsterStrikeWindupMs` = 400 ms, timed on the server. Bosses keep the
immediate contact strike and their ground slam; this decision does not change them.

**D2. The client is told when a wind-up starts.** The server pushes `StrikeTelegraph { MonsterId, DelayMs }` to every player on the instance. The
push carries no damage and no position: the client already knows where the monster stands from `MonsterChanged` and the snapshots.

**D3. The strike lands only if the player is still in reach.** At the end of the wind-up the server reads the monster's current tile and each
player's current tile. A player within one tile (Chebyshev, diagonals included, `CombatRules.IsMeleeInReach`) takes the contact damage; a player who
stepped away takes none. A monster that has been struck down (`Hp <= 0`) or removed during the wind-up does not strike. The wind-up flag clears
whatever the outcome, so a cancelled or failed wind-up never blocks the next one.

**D4. The windup is drawn by the engine.** The monster rises up to three pixels over the wind-up and flashes white every 80 ms (the existing
`Whiten` flag). Reduced motion keeps the flash and drops the rise (ADR-076). No overlay or new render command: the flash reuses the sprite path, so
the frame loop allocates nothing new. The client copies the windup list once per frame into a reused list (`CopyStrikeWindups`).

**D5. The cadence does not change.** A monster still strikes at most once per AI tick (1.5 s). The 400 ms wind-up sits inside that interval, so a
player who stays put takes the same number of hits per minute as before; the change is the warning, not the rate.

**D6. Touch prompts (same change, separate concern).** A finger gets the fishing bite prompt `Chạm để kéo cần!` (`Tap to reel in!`) instead of
`Nhấn Space hoặc bấm chuột để kéo cần!` (`Press Space or click to reel in!`). The device decides it, by the test that already marks a touch
device (`DeviceGate.IsTouchDevice`), through `DeviceGuardViewModel.IsTouchInput`; `FishingViewModel.TouchInput` is set once at start-up.

## Consequences

- Monster fights take one more second of lead time per strike. Players who stood still lose the same hit points over time; players who watch
  the flash can avoid the hit. Balance is unchanged (ADR-081 targets are untouched).
- protocol 32. Clients built for 30 ignore the unknown push and see no flash, so the server and client ship together.
- Only the wind-up is new. The strike's damage, defence and reply path (`ApplyMonsterContactAttackAsync`) are the existing ones.
- Tests: `CombatRulesTests` (400 ms, reach table), `CombatPacketTests` (round trip, opcode 0xC249), `MarketSortPacketTests` (version 31),
  `FishingViewModelTests` (touch prompt).

## Alternatives considered

- **A shorter wind-up (200 ms).** Too brief to see on a 60 Hz frame beside a sprite that is already moving.
- **Telegraph on the client alone.** The client does not know the server's AI tick, so the flash and the hit would drift apart. The server owns
  the time, as in ADR-047.
- **Damage at the wind-up start, with a dodge window.** That would make the hit land before the warning ended. The hit waits for the end.
