# ADR-009 — PostgreSQL persistence and player accounts

**Status:** Proposed

## Context

The server (ADR-003) keeps no state: every connection starts anonymous and nothing survives a
restart. Multiplayer, farming, inventory and the market all need players who persist and can
sign in again. The sibling product AtmosOS runs the same stack (.NET 10, Nalix, EF Core,
PostgreSQL) in production; the owner approved PostgreSQL for Nosum.

## Decision

- **D1 — Database.** PostgreSQL through EF Core 10 and the Npgsql provider, snake_case names
  (`EFCore.NamingConventions`). Packages are referenced only by `Nosum.Infrastructure` (and the
  design-time package by `Nosum.Host`).
- **D2 — Layout.** Entities in `Nosum.Domain`; contracts for services, the generic
  `IRepository<T>`/`IUnitOfWork`/`IUnitOfWorkFactory`, `IPasswordHasher` and
  `ISessionTokenService` in `Nosum.Abstractions`; EF mapping, migrations and implementations in
  `Nosum.Infrastructure`. Handlers keep depending on Abstractions only. One short unit of work
  per operation; future features add entities and a service on the same pattern.
- **D3 — Migrations** are generated with the `dotnet ef` tool (local tool manifest
  `.config/dotnet-tools.json`) into `Nosum.Infrastructure/Persistence/Migrations`, applied by the
  host at start-up. First migration: `InitialCreate` (`players`, `player_states`,
  `player_session_tokens`).
- **D4 — Configuration.** Connection string from `ConnectionStrings__Default`. Without it the
  host falls back to a local development database, except in Production
  (`DOTNET_ENVIRONMENT`/`ASPNETCORE_ENVIRONMENT=Production`), where it refuses to start.
- **D5 — Accounts.** A `Player` has an application-generated GUID, a unique case-insensitive
  username (3-20 ASCII letters, digits, `_`, starting with a letter; stored as typed plus a
  lower-case normalized column), a PBKDF2-HMAC-SHA256 password hash (600 000 iterations, 16-byte
  salt, self-describing string), a preferred language (BCP-47, default `vi`) and created /
  last-login timestamps. Passwords are 8-128 characters with no composition rule. `PlayerState`
  (map name, feet x/y, facing) is a separate 1:1 row, written by the multiplayer feature.
- **D6 — Sessions.** Register and login issue a random 256-bit token valid for 30 days; only its
  SHA-256 hash is stored. Resume with the token signs a connection in again; the token is not
  rotated. Register signs the new player in immediately.
- **D7 — Connection identity.** On success the handler stores a `PlayerIdentity` in the Nalix
  connection attributes and raises `Connection.Level` to `USER`; later handlers read it with
  `connection.GetPlayer()` and are gated by `[PacketPermission(PermissionLevel.USER)]`. Public
  handlers use `PermissionLevel.NONE`. `PermissionMiddleware` and `RateLimitMiddleware` are
  registered. Register/login are limited to a burst of 5 then 1 per second per client endpoint (Nalix `RateLimitMiddleware`), with
  a 60 s lockout after repeated violations.
- **D8 — AOT.** `Nosum.Infrastructure` opts out of `IsAotCompatible`/`IsTrimmable` because EF
  Core is reflection based. Other server libraries stay AOT compatible; the host is not published
  AOT.
- **D9 — Wire.** Account opcodes occupy band `0x1000-0x10FF`. Replies carry `AccountStatus`
  codes, never text (ADR-010 D2).
- **D10 — Tests.** Persistence and account tests run against a real PostgreSQL server
  (throw-away database per test class); no SQLite or in-memory provider is used.

## Alternatives considered

- **SQLite for tests** — no extra server, but different SQL semantics (unique violations,
  `timestamptz`, concurrency); real PostgreSQL was available and is what runs in production.
- **ASP.NET Core `PasswordHasher`/Identity** — pulls in a package set and a schema Nosum does not
  need; `Rfc2898DeriveBytes.Pbkdf2` is a BCL primitive.
- **Reuse Nalix `UseSessions`** — it resumes transport sessions, not accounts; accounts need
  their own durable token.
- **Argon2/bcrypt** — stronger against GPUs but need a third-party package.

## Consequences

- Hosting needs a PostgreSQL server; the host cannot start in Production without one.
- Brute-force protection is per client endpoint (rate limit); there is
  no per-account lockout, so a distributed guesser is limited only by the 600 000-iteration cost.
- The WebSocket carries plain-text passwords. Until `UseSecureConnections()` is enabled,
  confidentiality depends on TLS (`wss://`) terminated in front of the server (`deploy/`).
- Tokens cannot be revoked from the client yet (`revoked_at` exists; no logout packet).
- Server and client must ship together for the new packets; opcode band values are append-only.

## Revisit when

Accounts need email/OAuth sign-in, password reset, or per-account lockout; or when the host is
published AOT.
