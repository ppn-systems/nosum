# ADR-029 — Chat, friends and mail

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M4. Builds on ADR-009 (accounts,
nicknames, profanity filter), ADR-022 (chunks), ADR-019 (visiting).

## Context

Players can trade and share co-ops but cannot talk. An MMO needs chat with moderation, a friends list, and an
offline channel for system messages.

## Decision

- **D1 — Chat channels:** local (players in the sender's chunk interest set, ADR-022), map instance, co-op, and
  whisper (by nickname, to an online player). Messages are player-authored text: up to 160 characters, NFC
  normalised, control characters stripped, run through the profanity filter (masked, not rejected), rate-limited
  (5 messages / 10 s per player, burst 3) by the existing middleware. Guests may only use local chat.
- **D2 — Player text on the wire is the one exception to "codes only"** (ADR-010): it is shown verbatim, never
  translated; the client renders it as plain text (no markup). System lines are codes.
- **D3 — Moderation:** per-player block list (blocked players' chat and trade/visit requests are dropped server-side),
  report intent (stored with the last 20 messages of context for admins, M5), server-side mute (admin, M5).
  No chat history is persisted except reports.
- **D4 — Friends:** request/accept by nickname; mutual friends see each other's online status and current public map;
  friends are a visiting allow-list source (ADR-019). Persisted; one migration.
- **D5 — Mail:** a mailbox per player for system messages (sold listings, co-op invitations, community goal rewards,
  shop deliveries). Mail may carry items/coins collected by an atomic transfer. Codes + arguments, rendered with
  `.resx`. Unread count on the HUD (a small badge on the gear). Mail expires after 30 days.
- **D6 — Wire.** Player-to-player features share the Trade band: chat `0x9100–0x91FF`, friends `0x9200–0x92FF`,
  mail `0x9300–0x93FF`. Append-only; the coordinator bumps `NosumProtocol.Version` at merge.
- **D7 — Client.** A chat line at the bottom-left (collapsed to the last 3 lines fading out; Enter opens input), a
  friends page and a mail page in the game menu.

## Consequences

Chat is the first feature that sends user text to many clients: tests must cover masking, length limits, block
lists, and that whispers never reach a third party.
