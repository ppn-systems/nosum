# ADR-066: Farm guestbook and watering recognition

**Status:** Proposed (agent-written, 2026-10-29; owner approval pending). Builds on [ADR-019](ADR-019-visiting-private-instances.md) (visiting) and [ADR-024](ADR-024-crafting-and-outdoor-placeables.md) (crafted placeables). Answers issue #140.

## Context

[ADR-019](ADR-019-visiting-private-instances.md) lets a listed player walk into someone's farm and water crops (D3). The owner, however, never learns who came: a friend can water twelve crops and leave no trace. Issue #140 asks for a placeable where a host can see visitors' help, a log of that help, and an indicator that something is new when the host returns.

The issue also proposed a visitor "sign in" with praise emotes. That is not built here: it needs a fixed emote vocabulary on the wire and a rule for who may sign where, which is a separate design question.

## Decision

- **D1 — A craftable placeable.** `ItemCatalog.Guestbook` (606) is made by `RecipeCatalog.Guestbook` (Farming level 1; 30 wood and 10 fiber) and is a `PlaceableKind.Guestbook` placeable. It reuses the scarecrow's cell on `craftables.png` until it has art of its own (ADR-007).
- **D2 — A log of watering, one row per visitor and day.** `guestbook_entries (owner_id, visitor_id, day_number, waters, unread, updated_at)`, keyed by the first three columns. A watering by a visitor on somebody else's private farm is counted into the row for the current game day (`GameCalendar.DayNumberAt`) and marked unread. The owner's own watering is never logged (`GuestbookRules.Logs`).
  Logging is **gated on the placeable**: nothing is written while no guestbook stands on the farm, so the log exists only where the owner chose to put one. The check reads the farm's placed pieces in memory; no database read is added to the watering path.
- **D3 — The log is written after the crop change, and never fails the watering.** `FarmService` counts the watering once the inventory and crop change are committed. A failed write is swallowed (the watering stands); the next watering counts again.
- **D4 — Reading the log clears it.** `GetGuestbook` (C2S) replies with `GuestbookSnapshot`: the number of entries that were new, and the newest 30 days (`GuestbookRules.MaxRows`), each row saying whether it was new. The server marks every unread entry read in the same unit of work. The read is owner-only: the identity comes from the connection.
- **D5 — Concurrency.** A count is a read-modify-write, so the reads and writes of one owner's guestbook run under a per-owner in-process lock (`StripedAsyncLock`). The primary key on `(owner, visitor, day)` is the database backstop. This assumes one server process, as ADR-019's revisit list already does.
- **D6 — Wire (band `0xE000`, append-only).** C2S `GetGuestbook = 0xE006`; S2C `GuestbookSnapshot = 0xE042`; `GuestbookRow` is a plain row (no opcode). No existing packet or field changed, so `NosumProtocol.Version` is not bumped (as in ADR-019 D7).
- **D7 — Client.** A "Farm guestbook" section in the Visitors panel, shown to registered players only (a guest has no visitors, ADR-019 D2). It lists the newest entries ("{visitor} watered {n} crops on day {d}"), marks new ones and shows the unread count. The toolbar does not show a badge yet.

## Alternatives considered

- **One row per watering.** Rejected: a visitor who waters forty crops would write forty rows; a per-day count says what the host needs.
- **Log everything, no placeable.** Rejected: the issue asks for a placeable the owner chooses to place, and an unbounded log on every farm is noise.
- **Push the unread count when the owner logs in.** Deferred: reading when the panel opens already shows what is new; a login push needs a new notification path.
- **A cache of unread counts.** Rejected for now (ADR-035 D7): the log is read rarely, and a cache adds an invalidation on every watering.

## Consequences

- A new table `guestbook_entries` (migration `GuestbookEntries`), with cascade deletes from `players` for both sides.
- A visitor's watering on a farm with a guestbook costs one extra write in its own transaction, after the farm change. Owner watering and farms without a guestbook cost nothing extra. The DB budget has a row for `GetGuestbook` (2 reads, 1 SaveChanges).
- Entries are kept per game day indefinitely; there is no pruning yet.

## Revisit when

The owner approves the design or asks for the sign-in and emote part of the issue; a toolbar badge or a login push is wanted; the server runs as more than one process (D5); or the log grows and needs pruning.
