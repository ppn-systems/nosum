# ADR-032 — Security hardening before production

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M5. Builds on ADR-003 (Nalix handshake and
encryption), ADR-009 (accounts), ADR-029 (moderation), ADR-031 (operations).

## Context

The client accepts whatever server key the handshake presents, accounts have no recovery path, and handlers were
written by many agents in parallel. Before strangers connect, the trust model must be closed and audited.

## Decision

- **D1 — Server key pinning (TOFU + shipped pin).** The deploy publishes the server's static public key fingerprint
  into the client's `appsettings`/boot config; the client refuses a handshake whose key does not match. Development
  builds use trust-on-first-use stored in browser storage with a visible warning when it changes. Key rotation: the
  config may list two fingerprints during a rotation window.
- **D2 — Handler audit.** A test enumerates every registered opcode and asserts it has an explicit permission level and
  a rate-limit attribute (fail the build otherwise); a second test sends every C2S packet unauthenticated and asserts
  no state change and no reply beyond the documented status.
- **D3 — Input bounds.** Every C2S packet with strings or collections declares maximum lengths, enforced before the
  handler (a shared validator); tests fuzz each packet with oversized and malformed values.
- **D4 — Account recovery.** Registered (Gmail) accounts can reset their password with a one-time code sent by email
  through an `IEmailSender` seam (an SMTP implementation configured by environment; a logging fake in development).
  Codes are 8 digits, hashed at rest, valid 15 minutes, 5 attempts, rate-limited per account and per IP; a reset
  ends every session of the account.
- **D5 — Session hardening.** Session tokens rotate on resume; idle sessions expire after 30 days (as guests); a
  password change revokes every other token.
- **D6 — Abuse.** Per-IP connection caps and handshake rate limits (Nalix `ConnectionGuard` tiers) configured for
  production; new accounts per IP per day capped.
- **D7 — Dependency and secret hygiene.** A CI job runs `dotnet list package --vulnerable` and secret scanning; the build
  fails on high severity.

## Consequences

One new opcode slice for recovery (Account band append-only). The email provider is the only new external dependency
and stays behind the seam.
