# ADR-032 — Security hardening before production

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M5. Password sign-in and the password rules of D6 are replaced by docs/adr/ADR-069. Builds on ADR-003 (Nalix handshake and
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
- **D4 — Account recovery (removed).** Originally: a one-time e-mailed reset code for Gmail accounts. Removed on
  2026-10-29 by the owner's decision; see the amendment at the end of this record.
- **D5 — Session hardening.** Session tokens rotate on resume; idle sessions expire after 30 days (as guests); a
  password change revokes every other token.
- **D6 — Abuse.** Per-IP connection caps and handshake rate limits (Nalix `ConnectionGuard` tiers) configured for
  production; new accounts per IP per day capped.
- **D7 — Dependency and secret hygiene.** A CI job runs `dotnet list package --vulnerable` and secret scanning; the build
  fails on high severity.

## Consequences

Account recovery added one opcode slice and an e-mail provider dependency. Both were removed again on 2026-10-29 (D4); the
opcodes stay reserved.

## Implementation notes

Choices made where the decision above is silent (simplest safe option each time).

- **D1 — pin and TOFU.** The pin is the server's X25519 public key (64 hex digits; the "fingerprint" is the key itself, which is public).
  The host prints it at start-up (`Server public key …`) and keeps the private half in the file Nalix creates
  (`~/.local/share/Nalix/config/certificate.private`, or `NOSUM_SERVER_KEY_PATH`); a container must mount that path on a volume, or every restart
  changes the key and every pinned client refuses the server. The client reads `Server:PublicKeys:0|1` and `Server:RequirePin` from
  `wwwroot/appsettings.json`; the SDK verifies the server's handshake proof against the pinned key, and with two keys the client tries the one that
  worked last first. `Server:RequirePin=true` makes a missing key a start-up error instead of a silent fallback to TOFU. Without a pin, a development
  build asks the server for its key (`PUBLIC_KEY_REQUEST` control frame, answered with `SessionTofu`), pins to it for the handshake, and remembers it
  in `localStorage` only after the handshake succeeded. A handshake that fails against a pinned or remembered key triggers one more probe; a
  different answer puts the socket in `GameSocketState.KeyMismatch`, which `ConnectionBadge` shows in both languages. The remembered key is never
  replaced silently (clear the site data to start over).
- **D2 — audit.** `HandlerAuditTests` enumerates every `[PacketHandler]` class by reflection and fails on a handler without `[PacketPermission]`,
  `[PacketRateLimit]`, bounds on its packet, or `[PacketEncryption(true)]` while its packet carries a password, token or code; it pins the exact set of
  handlers reachable without signing in; it checks that every `…C2S` packet has a handler and that `Program.cs` maps every handler class.
  `SecurityWireTests` sends every `…C2S` packet from a connection that is not signed in (encrypted and plain) and asserts no game reply and no row
  changed in any table; the public handlers answer with their documented refusal only.
- **D3 — bounds.** Bounds are declared on the packet members with `[WireMaxLength]`, `[WireRange]` and `[WireDefined]` (`Nosum.Contracts.Validation`,
  constants in `WireLimits`) and enforced by `InputBoundsMiddleware` (order −60, before permission and rate limit) through `PacketBounds`, which compiles
  the declarations of a packet type into one delegate (no allocation per packet). Bounds are sanity limits, looser than the game rules (a nickname is 16
  characters, the bound is 64): the rules keep answering with status codes, the middleware only drops what no real client sends, silently, and counts an
  error on the connection. `PacketBoundsTests` fuzzes every `…C2S` packet member with oversized, out-of-range, NaN and undefined values. Booleans, bytes,
  GUIDs and unsigned numbers cannot be out of bounds and need no declaration; every other string, collection, signed or floating number and enum does.
- **D4 — recovery (removed, 2026-10-29).** The `RequestPasswordReset` (`0x11F0`) and `ConfirmPasswordReset` (`0x11EF`) packets, their handlers,
  the `password_reset_codes` table (dropped by migration `RemovePasswordRecovery`), `IEmailSender` with its SMTP and logging senders, the
  `NOSUM_SMTP_*` settings and the client form are gone. The opcodes and packet classes stay reserved and are never reused. `ChangePassword`
  (`0x11EE`) and its `PasswordResult` reply (`0x11E0`) are unaffected. Per-address abuse limits for recovery went with the handlers.
- **D5 — sessions.** Every `ResumeSession` issues a new token and a fresh 30-day window (idle expiry is simply expiry without a resume); the replaced
  token keeps working for 15 minutes (amended by issue #171: the grace was 2 minutes, which locked out a guest whose rotation reply was lost) so a lost reply can be retried, then dies. One account keeps at most 10 live tokens; issuing another revokes the
  oldest. `ChangePassword` revokes the other tokens. Revoked and expired token rows are swept by the hourly `GuestCleanupService`. The client already replaces its in-memory token with the one a resume returns (`AccountSessionKeeper`).
- **D6 — abuse.** `ConnectionQuotaOptions` (the Nalix connection guard) is set from `AbuseLimits` (20 connections per address, 20 per 5 s, 200 per subnet,
  each overridable by `NOSUM_MAX_CONNECTIONS_PER_IP`, `…_PER_WINDOW`, `…_PER_SUBNET`, `NOSUM_MAX_SUBNET_CONNECTIONS_PER_WINDOW`); new accounts
  (registered and guest) are capped at 30 per address per day by `IAbuseGuard` (`NOSUM_NEW_ACCOUNTS_PER_IP_PER_DAY`). Loopback is exempt unless
  `NOSUM_ABUSE_ENFORCE_LOOPBACK=true`: behind a reverse proxy on the same host, forward the real client address or turn the exemption off, or every
  player shares one key.
- **D7 — hygiene.** `.github/workflows/security.yml` runs `.github/scripts/audit-packages.sh` (`dotnet list package --vulnerable --include-transitive`
  for every solution, failing on High or Critical) and gitleaks over the full history (`.gitleaks.toml` allows only the local development password);
  both also run weekly.
- **Not done here.** A settings-page form for `ChangePassword` (the packet and handler exist; the screen
  belongs with the settings work of ADR-033); per-account failed-login lockout (the per-address `account-credential` rate limit is the only brake
  on password guessing today); e-mail change; two-factor sign-in.
