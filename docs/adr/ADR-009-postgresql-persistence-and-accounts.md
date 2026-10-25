# ADR-009 — PostgreSQL persistence and player accounts

**Status:** Accepted (owner decisions, 2026-10-25)

## Context

The server (ADR-003) keeps no state: every connection starts anonymous and nothing survives a
restart. Multiplayer, farming, inventory and the market all need players who persist and can
sign in again. The sibling product AtmosOS runs the same stack (.NET 10, Nalix, EF Core,
PostgreSQL) in production; the owner approved PostgreSQL for Nosum and decided the account model
below.

## Decision

- **D1 — Database.** PostgreSQL through EF Core 10 and the Npgsql provider, snake_case names
  (`EFCore.NamingConventions`). Packages are referenced only by `Nosum.Infrastructure` (and the
  design-time package by `Nosum.Host`).
- **D2 — Layout.** Entities in `Nosum.Domain`; contracts for services, the generic
  `IRepository<T>`/`IUnitOfWork`/`IUnitOfWorkFactory`, `IPasswordHasher` and
  `ISessionTokenService` in `Nosum.Abstractions`; EF mapping, migrations and implementations in
  `Nosum.Infrastructure`. Handlers keep depending on Abstractions only. One short unit of work
  per operation; future features add entities and a service on the same pattern.
  `Nosum.Domain` may reference `Nosum.Contracts` (for `GameLanguage`).
- **D3 — Migrations** are generated with the `dotnet ef` tool (local tool manifest
  `.config/dotnet-tools.json`) into `Nosum.Infrastructure/Persistence/Migrations` and applied by
  the host at start-up. `InitialCreate` created the first schema; `GuestAccountsEmailLanguage`
  moved it to guests, e-mail logins and the language enum. That migration drops the earlier
  username and string-language columns: the project had no deployed data when it was made.
- **D4 — Configuration.** Connection string from `ConnectionStrings__Default`. Without it the
  host falls back to a local development database, except in Production
  (`DOTNET_ENVIRONMENT`/`ASPNETCORE_ENVIRONMENT=Production`), where it refuses to start.
- **D5 — Login is a Gmail address.** The login is an `@gmail.com` address (case-insensitive,
  stored as typed plus a lower-case normalized column with a unique index). Local part: 6-30
  ASCII letters, digits and dots, starting and ending with a letter or digit, no `..`, no `+`
  tags. Passwords are 8-128 characters with no composition rule, hashed with PBKDF2-HMAC-SHA256
  (600 000 iterations, 16-byte salt, self-describing string). There is no e-mail verification
  yet: anyone can register any unused Gmail address (open item).
- **D6 — Guests.** A player can start as a guest (no e-mail, no password): the server creates a
  `Player` with `IsGuest = true` and a session token, so the guest can resume. Registering later
  (`UpgradeGuest`, only on a guest session) upgrades the same `Player` in place: id, state and
  anything attached to the player stay; the guest's token is revoked and a new one issued.
  The quest feature unlocks the invitation to register through `IRegistrationPromptService`;
  the flag is stored (`registration_prompt_unlocked_at`) and reaches the client in
  `AccountResult.RegistrationPromptUnlocked`. A new registration (not an upgrade) signs in
  immediately and starts a new game.
- **D7 — Idle guests are deleted.** `GuestCleanupService` runs hourly in the host and deletes
  guests whose `LastActiveAt` is older than 30 days (`GuestCleanupService.GuestRetention`),
  with their state and tokens (cascade). Registered players are never touched. `LastActiveAt`
  is set at creation and on every login and resume; features that keep a player busy for long
  call `IPlayerActivityService.TouchAsync`.
- **D8 — Sessions.** Register, login and guest start issue a random 256-bit token valid for
  30 days; only its SHA-256 hash is stored. Resume with the token signs a connection in again
  (the token is not rotated; an upgrade does issue a new one). `Logout` revokes the session's
  token (`revoked_at`) and signs the connection out; resume with it then fails.
- **D9 — One active session per player.** `ISessionRegistry` keeps one connection per player.
  A new login, resume or guest start for a player who is connected pushes `SessionEnded`
  (`AccountStatus.SessionReplaced`) to the older connection, makes it leave its map (its
  position is saved once there), drops its permission level and closes it, all before the new
  connection continues. The client shows localized text and returns to the account form.
- **D10 — Connection identity and gating.** On success the handler stores a `PlayerIdentity`
  (player id, session id) in the Nalix connection attributes and raises `Connection.Level` to
  `USER`; later handlers read it with `connection.GetPlayer()` and are gated by
  `[PacketPermission(PermissionLevel.USER)]`. Public handlers use `PermissionLevel.NONE`.
  `PermissionMiddleware` and `RateLimitMiddleware` are registered. Register, login and upgrade
  are limited to a burst of 5 then 1 per second per client endpoint with a 60 s lockout after
  repeated violations; guest start to a burst of 3.
- **D11 — Transport security.** The server runs Nalix `UseSecureConnections()` and clients
  `HandshakeAsync()` (it also keeps simple bots, which cannot complete the handshake, out).
  Every account handler that carries a password or a token is marked
  `[PacketEncryption(true)]`, so plaintext frames are refused, and the client sends requests
  encrypted. TLS (`wss://`) terminated at the reverse proxy stays as well. Clients trust the
  server key on first use (TOFU) for now; pinning `TransportOptions.ServerPublicKey` is required
  before production (open item).
- **D12 — Languages.** A player's language is a `GameLanguage` enum (Unspecified = Vietnamese,
  Vietnamese, English) on the wire and in code, stored as a smallint (compact; the numbers are
  append-only); `GameLanguages` maps it to BCP-47 tags only for culture names. See ADR-010.
  Replies carry `AccountStatus` codes, never text.
- **D13 — AOT.** `Nosum.Infrastructure` opts out of `IsAotCompatible`/`IsTrimmable` because EF
  Core is reflection based. Other server libraries stay AOT compatible; the host is not
  published AOT.
- **D14 — Wire.** Account opcodes occupy band `0x1000-0x10FF`. Bands: Account `0x1000`,
  Player `0x2000`, Farm `0x3000`, Market `0x4000`.
- **D15 — Tests.** Persistence and account tests run against a real PostgreSQL server
  (throw-away database per test class); no SQLite or in-memory provider is used.

## Alternatives considered

- **SQLite for tests** — no extra server, but different SQL semantics (unique violations,
  `timestamptz`, concurrency); real PostgreSQL was available and is what runs in production.
- **ASP.NET Core `PasswordHasher`/Identity** — pulls in a package set and a schema Nosum does not
  need; `Rfc2898DeriveBytes.Pbkdf2` is a BCL primitive.
- **Reuse Nalix `UseSessions`** — it resumes transport sessions, not accounts; accounts need
  their own durable token.
- **Argon2/bcrypt** — stronger against GPUs but need a third-party package.
- **A new `Player` on register after a guest** — would lose the guest's progress.

## Consequences

- Hosting needs a PostgreSQL server; the host cannot start in Production without one.
- Brute-force protection is per client endpoint (rate limit); there is no per-account lockout.
- Gmail dot variants (`a.b@` and `ab@` are one mailbox) are different logins here.
- Server and client must ship together for the account packets; opcode and enum values are
  append-only.

## Revisit when

Accounts need e-mail verification, other sign-in providers or password reset; a per-account
lockout; a pinned server key is set up; or the host runs on more than one process (the session
registry and map presence are in-memory).

## Amendments

### 2026-10-25 - nicknames (owner decision)

- **Every player has a display nickname.** `players.nickname` (as chosen, NFC), `normalized_nickname` (unique index) and `nickname_changed_at`. Rules (`NicknameRules`, shared by
  server and client): trimmed and NFC-normalized, 3 to 16 characters, Unicode letters (Vietnamese diacritics included), ASCII digits, single spaces or underscores between them
  (no leading, trailing or doubled separator), at least one letter. The uniqueness key is NFC, lower case, underscore read as a space, so "Minh_Anh" and "minh anh" are the same
  name while "Minh" and "Mình" are different. A reserved list (admin, moderator, system, staff, gm, support, nosum, guest, khach, quản trị, hệ thống, ...) is compared after folding
  (lower case, no diacritics, no separators, no digits), so "Ádmin", "Admin_2" and "Quản Trị" are refused too.
- **Registration and guest upgrade ask for it.** `Register.Nickname` and `UpgradeGuest.Nickname` (appended fields; `NosumProtocol.Version` is now 3). Refusals are `AccountStatus`
  codes `InvalidNickname`, `NicknameReserved`, `NicknameTaken`. A lost race on the unique index is a normal `NicknameTaken` (or `EmailTaken` when the e-mail was the clash).
- **Guests.** The server stores a neutral `guest-NNNN` (4 random digits, wider after a few clashes); a hyphen is not allowed in a chosen name, so a player can never type one. The
  client renders the localized prefix from `Nickname_Guest` ([ADR-010](ADR-010-internationalization.md): no display text on the wire). `AccountResult.Nickname` carries it.
- **Rename.** `SetNickname` / `NicknameResult` (opcodes `0x1008` / `0x1086`, encrypted, USER, rate limited) once every 24 hours; the first change is free; asking for the current
  nickname changes nothing and does not start the wait; a per-player lock keeps two parallel requests from both passing the wait. The reply carries when the next change is
  allowed. After a rename the map is told through the look row ([ADR-014 amendments](ADR-014-coops-and-looks.md)). Migration `PlayerNicknames` gives existing players
  `guest-0001`, `guest-0002`, ... in creation order.
- **Cache.** `INicknameDirectory` resolves account id to nickname (in memory, filled on first read, updated by the account service) for looks, market sellers and co-op rows.
- **Open owner questions:** whether letters of other scripts should be refused (Cyrillic or Greek look-alikes of Latin letters are accepted and are not caught by the reserved
  list); whether guests may rename (built: yes, same wait); a profanity list; whether "Minh" and "Mình" should be one name.
