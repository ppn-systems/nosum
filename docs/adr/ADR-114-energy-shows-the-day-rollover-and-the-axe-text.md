# ADR-114: Energy shows the day's refill as it happens, and the axe text names a count the game keeps

## Status

Accepted

Amends [ADR-023](ADR-023-energy-skills-food.md) (D4: the refill is shown on the client as soon as the game day changes, not at the next
push) and [ADR-089](ADR-089-gathering-review-tier-energy-and-feedback.md) (the axe description's swing count). No wire change: `EnergyChanged`,
`NosumProtocol.Version` (34) and every opcode stay as they are.

## Context

Round three of the gathering critique ([critique-gathering.md](../design/critique-gathering.md) section 7d, finding G10) found that the energy
bar read 0 after the game day changed, until the first charge. The server refills energy on the first read or charge after the day changes
(`EnergyRules.Refill`), and it pushes `EnergyChanged` only after a charge or at sign-in. The client kept the last push, so a farmer who had
spent everything the day before looked exhausted all morning: the bar read 0, the swing was slowed and the sweat bubble showed, and running was
refused by the client's run gate (`RunStateRules.Resolve` reads the same value).

Separately, the axe description said a full-grown tree takes "about ten swings". The measured count with a basic axe is twelve (ten to fell and
two for the stump; critique section 7, G9).

## Decision

- **D1 - The shown energy follows the game day.** `EnergyRules.Shown(energy, maxEnergy, readDay, today)` is the refill rule as a client reads it: a
  reading taken on an earlier game day shows the largest energy, because the server refills on its next read. `ProgressChannel` records the game
  day of each `EnergyChanged` (from the calendar it reads once at sign-in), and `Energy` is `Shown(...)`. The rule is pure and lives in
  `Nosum.Gameplay`, so the server's refill and the client's display share one definition. A reading taken before the calendar is known is not
  rolled over until the calendar loads; the channel then takes that day as the reading day, since sign-in reads both at once.
- **D2 - The HUD repaints at the boundary.** `ProgressChannel` arms a one-shot timer for the next game day start (plus 50 ms; it re-arms a turn
  that fires early, at least 250 ms apart) and raises `Changed`, so the energy bar, the exhaustion state and the run gate follow the day without a
  push. The server stays authoritative: a use is still charged and refilled on the server, and a client that is wrong about the day is corrected
  by the next push.
- **D3 - The axe text states the count.** "A full-grown tree takes about a dozen swings with a basic axe." (vi: "khoảng mười hai nhát với rìu thường").
  The number is the measured one, not the code's (the stump is counted); the tooltip wording of section 7b is unchanged.
- **D4 - Button kinds are not changed.** Critique inventory F10 reported `plain` and `link` variants that the standard did not name. On master the
  code uses `primary`, `secondary`, `danger`, `tab` and `icon` only, and `ui-standard.md` section 8 lists the same five; the finding was already
  closed and no change is needed.

## Consequences

- After midnight the energy bar shows full and the farmer is no longer shown exhausted, without the player spending anything. The first use still
  reaches the server, which refills and charges; the displayed value and the server's agree.
- The rule is pinned by `EnergyShownTests` (same day, later day, unknown reading day, a reading from days back). The channel's timer is not unit
  tested: it needs a clock the tests can move, which the channel does not take. A live midnight is not captured in the QA frames because the client reads
  the calendar once at sign-in and the QA hook can only move the server's clock (see the round-three README).
- Captures: [docs/qa/gathering-round3/](../qa/gathering-round3/README.md) (1280×720, 1920×1080, 390×844 in vi and en): the HUD after four tills.

## Revisit when

- A live day change needs proof in a browser: the QA hook should move the client's calendar too (a reload of the calendar at the QA advance), not only
  the server's. That is a hook change (ADR-079).
- The energy refill moves to a server push at midnight. That is a wire change and needs its own ADR and a protocol bump.
