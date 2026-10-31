# ADR-008 — Movement sync: client-predicted intents, full-state snapshots at 10 Hz

**Status:** Proposed

## Context

Two or more players on one map must see each other walk ([pillars](../design/pillars.md): a
shared world; [ADR-003](ADR-003-server-authoritative-websocket.md): the server decides). Transport
is one reliable, ordered WebSocket, so there is no packet loss to design around, only latency and
jitter. The server must not trust a client's position.

## Decision

- **D1 — Client predicts, server validates.** The client walks its own farmer with the same
  `Walker` the server's rules are built on, so input feels instant. About every 100 ms (only when
  the state changed) it sends a `MoveIntent`: feet position, facing, walking flag, and a counter.
- **D2 — Validation is a budget, not a per-packet speed check.** `MoveValidator`
  (`Nosum.Gameplay`) earns `Walker.Speed` pixels per second of server time into a budget capped at
  0.5 s of walking, and each accepted claim spends the distance it covers. This tolerates network
  jitter that bunches claims together and still caps the average speed. A claim is also refused
  when it is outside the map, when its footprint overlaps a blocked tile, or when the way to it
  (straight line or either one-bend path) crosses one, so a wall cannot be jumped with saved budget.
  The walk grid is built by the same code on both sides (`FarmProps.BuildWalkGrid`).
- **D3 — A refusal is a correction.** The server keeps the old position and pushes a
  `MoveCorrection` (code and position) to that player; the client snaps back and ignores corrections
  for intents that were already in flight.
- **D4 — Full-state snapshots, 10 per second.** While anyone is on a map, one loop pushes a
  `MapSnapshot` (everyone on the map, with a server timestamp) to every player there. Full state
  makes a late snapshot harmless and a left player self-evident; a `PlayerLeft` notice just
  removes them sooner. 10 Hz is a starting point (about 20 bytes per player per push).
- **D5 — Remote players are drawn in the past.** The client keeps the last 8 samples per remote
  player (`SnapshotBuffer`) and draws them 200 ms behind the estimated server clock
  (`ServerClock`), interpolating between two real samples; nothing is extrapolated. Facing and
  walking come from the older sample, so animation matches position.
- **D6 — Reusable pieces.** `IMapPresence` (server: who is on a map, one Nalix group per map,
  multicast), `GameSocket`/`PlayerChannel` (client transport) and `SnapshotBuffer` (engine) know
  nothing about farming; later features reuse them.

## Alternatives considered

- **Server simulates from input state (client sends held keys).** Strongest authority, but needs
  reconciliation and replay of unacknowledged inputs on the client; more code than a first slice needs.
- **Delta snapshots.** Smaller, but a lost or reordered delta corrupts state and needs acks; the
  player counts expected per map do not justify it yet.
- **Higher tick rate (20-30 Hz).** Smoother, more bandwidth; the interpolation delay already hides
  10 Hz. Revisit with measurements.

## Consequences

- Other players are shown about 0.2 s plus latency in the past; the local farmer is never delayed.
- A cheater can still walk at full speed through anything legal and can hoard up to 0.5 s of
  walking; positions that are out of reach, blocked or too fast are refused.
- Two tabs signed in as the same account both appear on the map (the last to leave saves its position).
- Bandwidth grows with players per map squared (everyone receives everyone); a busy public map
  needs interest management or splitting ([pillars](../design/pillars.md), open question).

## Revisit when

A map holds more than a few dozen players at once, or measured jitter makes 200 ms too short.

## Amendment: footwear and the combined speed multiplier (ADR-082)

The validator's `speedFactor` is the player's combined multiplier: worn boots × speed buff × running, clamped to 1.5 (`MovementSpeed.Combine`,
docs/adr/ADR-082 D3). The budget, the slack, the corrections and the path checks are unchanged.
