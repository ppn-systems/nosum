# ADR-079: Development QA hook for the UI capture harness

## Status

Accepted

## Context

The UI capture harness (a Playwright run that screenshots every screen at three sizes, `blazor-ui`) has to reach states a fresh account never
reaches: a tile on every map, a season that has turned, a level-up banner, a line that has bitten. Playing into those states takes hours of game
time. The tester tools already exist: `DevGrant` and `DevAdvanceClock` (docs/adr/ADR-021 D7, `0xF000-0xF001`) are mapped only when the host runs in
the Development environment, and the client exports them only from a build made with `-p:NosumDevTools=true`. This ADR adds the four states the
harness still needs and a query flag that makes the client expose them to a browser script.

The rules that stay fixed: the server decides every outcome (docs/adr/ADR-003, `gameplay-rules`), a Production host maps no tester opcode
(`nalix-server-hosting`), and the wire is append-only (`nalix-packets`).

## Decision

- **D1 — Four more Development opcodes.** `DevTeleport` (`0xF002`), `DevSeason` (`0xF003`), `DevLevelUp` (`0xF004`) and `DevFishingBite` (`0xF005`) are
  requests in the `DevOpCode` band; their replies are `0xF042-0xF045`. They are handled by `DevHandlers`, which the host maps only in Development
  (`Program.BuildApplication(..., development: true)`). `NosumProtocol.Version` moves from 21 to 22. No existing packet, opcode or field changed.
- **D2 — Teleport is a travel to a tile.** `IPlayerService.TeleportAsync` takes a map id and a tile. It refuses a co-op land, an unknown map, a tile
  off the map and a copy the access policy does not admit, and then runs the steps of `TravelAsync` (leave the old place, `MapChanged`, enter the
  new one, save the place). The copy is the player's own private map or the shared public one. It is the same arrival rule as a warp
  (`WarpRule.Arrival`), so the player lands on a walkable tile near the one asked for.
- **D3 — The season passes on the server clock.** `IDevClockService.AdvanceSeasons` moves the clock to the start of the next season, once per season
  asked for (1 to 4), and delivers the day-ended events it crossed, exactly like `AdvanceDays`. Season starts fall on day boundaries, so the jump is
  a whole number of game days.
- **D4 — The banner does not change the skill.** `DevLevelUp` pushes a `LevelUp` packet to the sender. It exists to show the banner; the stored skill
  and its experience are left alone, so a test can show level 2 and still start from level 1.
- **D5 — A bite now, on the server and on the screen.** `IFishingService.MakeBiteAsync` replaces the sender's open cast with the same cast and a
  bite delay of zero. Only the player's own open cast on the same place counts. The client's bite cue follows the reply: the banner arms the bite
  at once (`FishingViewModel.BiteNow`). The judging rule of docs/adr/ADR-078 is unchanged, so a reel-in still has to fall inside the window.
- **D6 — The client hook is a query flag, compiled only for the tester build.** `QaEntry` exists only under `NOSUM_DEV_TOOLS`. At start it runs a
  fixed script that checks `?qa=1` and, only then, sets `window.nosumQa` with `grant`, `advanceClock`, `teleport`, `season`, `levelUp` and
  `fishingBite`, each calling a `DevTools` export. Without the flag the script does nothing and draws nothing. A production build contains neither
  the class nor the script (checked: the built `Nosum.Wasm.dll` has no `nosumQa` string).
- **D7 — Production refuses every tester opcode.** An integration test starts a host with `development: false` and probes each of the four new
  requests with a short timeout; none is answered. A unit test covers the guard `Program.IsDevelopment`: only an explicit `Development` turns it on,
  and an unset or other environment never does.

## Consequences

- The harness can reach every screen from a fixed set of calls, with no hand-written saves and no waiting for game time.
- Every tester call is a server request that a Development host answers and a Production host ignores. A Production client cannot ask: the
  tester code is not compiled into it.
- The bite and the banner are QA shortcuts. A screenshot of a bite is a real state of the client; a catch from it is a real server outcome;
  a level-up banner is not a level-up.
- Protocol version 22 is a wire change for every client built from this commit; clients and server ship together, as before.

## References

- docs/adr/ADR-003 (server authoritative), ADR-018 (seasons), ADR-021 D7 (game-day clock and the first tester tools), ADR-078 (fishing casts and the
  reel-in window), ADR-013 amendments (the tester grant).
- `server/Nosum.Handlers/Dev/DevHandlers.cs`, `contracts/Nosum.Contracts/Dev/`, `wasm/Nosum.Wasm/Game/QaEntry.cs`, `wasm/Nosum.Wasm/Game/DevTools.cs`.

## Amendments

### 2026-10-31 - the client re-reads the calendar after a clock jump (issue #248)

- **D5 — A server clock jump is read back.** The client estimates the server clock as its own clock plus an offset read once at sign-in
  (`CalendarChannel`, docs/adr/ADR-018). `DevSeason` and `DevAdvanceClock` move the server clock, so the offset went stale: the calendar label stayed
  on the old season and the season banner never came. `DevChannel` now asks for the calendar again after each of those replies, so the label, the
  day and the banner follow within one reply. No wire change: `GetCalendar` already carries the server clock.
- The season banner is raised by the same comparison as before (`CalendarViewModel.SeasonChanged`); only the stale offset was missing. A real season
  boundary needs no jump, so the local calculation already crossed it.

### 2026-11-01 - a Spring reset, a teleport next to a monster, and a named refusal for a silent skill press (world pass 3)

- **D3 — `DevSeason` with zero passes to the next Spring.** `IDevClockService.AdvanceSeasons(0)` moves the clock to the next Spring, one pass per season
  between the current season and Spring (0 to 3; zero passes in Spring). It is a Development-only server behaviour change: the packet, its fields and
  the protocol version are unchanged. The client calls it as `window.nosumQa.springReset()` at the start of a QA run. Out-of-range values (below 0 or
  above 4) still change nothing.
- **D8 — a skill press that the client refuses says why.** `PlayerActions` checked Z (Whirlwind), X (Fireball) and V (heal) against the same rules as
  the server and dropped a refused press without a word, so Z with no monster within two tiles did nothing visible. The refusal is now decided by
  `SkillPressRefusal.KeyFor` (cooldown, too little MP, no monster in reach, full health for the heal) and shown as a toast through
  `GameLoop.SkillPressRefused`. A cast already in flight still does nothing, silently, because the reply shows its result. The server's own refusals
  already had toasts (`SkillRefusalText`); the client check runs first only so the press is never silent before it is sent. The server still decides.
- **D9 — a QA teleport next to a monster.** `window.nosumQa.teleportToMonster(mapId)` moves the farmer onto the tile of a living monster the client
  already holds for the current map (the server places the farmer beside it). Development build only, like the other tester calls.
- **Beach coordinates.** The Beach's sand row above the sea is `y = 43` (`art/own/Maps/Beach.tmx`: the sea starts at `y = 44`). The world pass used
  `(39, 2)`, which is the Town road. A rod casts only by a click on water (ADR-078 D2); the action key does nothing with a rod, so a QA cast is a mouse
  click on the sea.

### 2026-11-02 - zero season passes reach the server again (world pass 7)

- **D10 — `DevSeason` accepts zero.** The packet's wire range was 1 to 4, so the server refused `Seasons = 0` before the handler ran. The
  `springReset()` call of D3 (the "next Spring" request every QA run starts with) was therefore dropped with no reply and did nothing: from Summer,
  Fall or Winter the season label stayed put, and a probe read the old season after the reset (the pass-5 "Fall after a Spring reset" report).
  The range is now 0 to 4 (`DevSeason.Seasons`). The layout is unchanged: only the validation changed, so the wire is the same and no opcode or
  protocol version moved. `DevSeasonPacketTests` pins the range, and `DevSeason_ZeroPasses_IsAnsweredAsTheNextSpring` checks the server answers.
  Evidence: `docs/qa/world-pass7/season-label/`.
