# ADR-018 — Seasons and the server calendar

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Review notes: 12 real hours per game day and the 2026-11-02 epoch accepted; withering of mature crops at season change kept (genre norm); parsnip stays plantable in winter only as the tutorial crop (ADR-025 roster otherwise has no winter crops); growth in game days is ADR-021 D7, done in a follow-up.

## Context

Pillar 3 ([`docs/design/pillars.md`](../design/pillars.md)) says everyone on a server lives in the same calendar, computed from server time,
and that seasonal crops make the market move. The owner decided (2026-10-25): one season lasts **two real weeks**, the season and day are the
same for every player, and there is no per-player clock. Crop growth is already computed from stored timestamps
([ADR-003](ADR-003-server-authoritative-websocket.md) D4, [ADR-011](ADR-011-inventory-farming-data-model.md)); nothing simulates offline farms.

## Decision

- **D1 — The calendar is two numbers.** `Nosum.Gameplay.Calendar.GameCalendar` is a pure function from a UTC instant to
  `GameDate(year, season, day of season, minute of day)`. It is fully described by an **epoch** (start of year 1, spring, day 1, 00:00 UTC) and the
  **real length of a game day**. Instants before the epoch count as the epoch. No clock is read inside it; the server passes its `TimeProvider` time in.
- **D2 — 28 game days of 12 real hours.** A season is 28 game days (`GameCalendar.DaysPerSeason`), a game day is 12 real hours
  (`DefaultDayLengthMs`), so a season is exactly 14 real days and a year (four seasons) 56 real days. A game day shows 24 game hours, so one game
  hour is 30 real minutes and one game minute 30 real seconds. Reasons: 28 days is the genre's familiar season size and gives events and goals a
  fine grain; 12 h means every player, whatever their time zone, sees both a day and a night phase within one real day; a 24 h day (14 days per
  season) would make the in-game clock just the real UTC clock. The default epoch is Monday 2026-11-02 00:00 UTC; the host reads `NOSUM_CALENDAR_EPOCH`
  (ISO 8601) to change it and refuses to start on an unreadable value. Year numbers start at 1. **Owner question:** day length and epoch.
- **D3 — Seasons are crop data.** `CropDefinition.Seasons` (`SeasonSet`, flags) lists the seasons a crop grows in; there is no per-crop branch in code.
  Defaults: parsnip spring + fall + winter, radish summer + fall, cauliflower spring + summer. Parsnip also grows in winter so a new player in any
  season can plant the tutorial crop and winter is not an empty season; every season has at least one crop (unit-tested).
- **D4 — Planting out of season is refused, crops wither at the season change.** `FarmRules.Plant` takes the calendar and returns
  `FarmVerdict.OutOfSeason` (wire `FarmStatus.OutOfSeason`). A crop **withers** at the start of the first later season it does not grow in
  (`GameCalendar.WitherAtMs(plantedAt, seasons)`), mature or not — the genre's rule, and the reason market prices of out-of-season produce should
  rise. Withering is computed from `PlantedAtMs` alone (`FarmRules.IsWithered`): no tick, no migration, nothing stored, and it never reverses when its
  season comes round again. A withered crop cannot be harvested (`FarmStatus.Withered`); planting a new seed on that soil replaces it. The client
  stops drawing withered crops (there is no withered sprite yet). Growth stays timestamp-based and unchanged. **Owner question:** wither mature
  crops, or let a mature crop wait for the player?
- **D5 — The seed shop sells in season.** `ShopRules.IsInSeason(itemId, calendar, now)`: a seed is sold only while its crop can be planted that
  season; every other item always. `ShopService.BuyAsync` refuses with `ShopStatus.OutOfSeason` before any coin moves. The shop UI may later show
  such seeds disabled; the server check is the rule.
- **D6 — The client computes the clock locally.** New wire band **`0xD000-0xD0FF` (Season)**: `GetCalendar` (C2S `0xD000`) and `CalendarInfo`
  (S2C `0xD040`: `EpochMs`, `DayLengthMs`, `ServerNowMs`). The client asks once per sign-in, rebuilds the same `GameCalendar` and offsets its own clock
  by `ServerNowMs`; the server pushes nothing at a season change. Only new opcodes were added (and new values on `FarmStatus` and `ShopStatus`
  bytes), so `NosumProtocol.Version` is not bumped by this change.
- **D7 — HUD widget.** `CalendarClock` (shared controls) with `CalendarViewModel` shows season, day of season and time, updating when the text
  changes (time in ten game-minute steps, five real minutes). Names and formats are `.resx` keys (`Season_*`, `Calendar_*`); time uses the current
  culture's short time format.

## Alternatives considered

- **A tick or season-change push from the server.** More packets and state for something both sides derive from two numbers.
- **A 24 h game day (14 days per season).** Simpler maths but the game clock equals the real clock; day and night would depend on the player's time zone.
- **Per-player clock** (the genre's default). Rejected by the owner: pillar 3 needs one shared calendar.
- **Stopping growth instead of withering.** Gentler, but the market would never feel the season end; a stopped crop also needs a stored flag.

## Consequences

- One shared calendar, no storage and no migration; changing the epoch or day length after launch moves every season boundary for everyone, so it is
  set once before launch.
- Crops left unharvested die at the season change; players who are offline for a season lose that field. Tutorial and economy balance depend on the
  crop-to-season table in `CropCatalog`.
- Out-of-season produce can still be sold at the shipping bin and the market; only seeds are gated.
- Season goals (community goals, pillar 3) and a time-of-day tint of the world are not built; both read the same `GameCalendar`.

## Revisit when

- The owner settles the day length, the epoch and the withering rule (open questions in `docs/design/pillars.md`).
- Season goals, seasonal prices or events are designed.
- A second server (shard) needs its own epoch.
