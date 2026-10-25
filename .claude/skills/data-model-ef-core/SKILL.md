---
name: data-model-ef-core
description: Use when creating or changing a persisted entity, NosumDbContext, IEntityTypeConfiguration, EF Core migration, repository/unit-of-work use, password or token handling, or the player-account flow. PostgreSQL, snake_case, generate-never-hand-write migrations, current-player accessor.
---

# Data model and EF Core (server/)

Decision: `docs/adr/ADR-009-postgresql-persistence-and-accounts.md`. Gameplay validation of what
is stored: `gameplay-rules`. Handler mechanics: `nalix-handlers`.

## Non-negotiables

- **Entities in `Nosum.Domain/{Domain}/`**, plain classes, no EF attributes. Mapping lives in
  `Nosum.Infrastructure/Persistence/Configurations/`, one `IEntityTypeConfiguration<T>` per
  entity (picked up by `ApplyConfigurationsFromAssembly`). Table names plural snake_case
  (`players`); column names come from the snake_case convention.
- **Ids are application-generated GUIDs** (`Entity.Id` = `Guid.CreateVersion7()`), keys
  `ValueGeneratedNever`. A 1:1 child (e.g. `PlayerState`) uses the parent id as its key.
- **Every entity or mapping change needs a generated migration** — model and schema move
  together. Never hand-write one; a migration without its `.Designer.cs` is silently ignored.
  ```bash
  dotnet tool restore
  dotnet ef migrations add <Name> --output-dir Persistence/Migrations \
    --project server/Nosum.Infrastructure --startup-project server/Nosum.Host
  dotnet ef database update --project server/Nosum.Infrastructure --startup-project server/Nosum.Host
  ```
  Review the generated diff (a one-column change that touches other tables means a stale
  snapshot), then check the schema in PostgreSQL (`\d table`). Output dir must stay
  `Persistence/Migrations` (its `.editorconfig` marks the code generated).
- **Handlers never see EF.** They call a service interface from `Nosum.Abstractions`; services
  in `Nosum.Infrastructure` use `IUnitOfWorkFactory`. One unit of work per operation:
  `Create()`, load, change, `SaveChangesAsync`, dispose. Never keep one across requests.
- **New feature data = new entities + a service**, built on `IRepository<T>`/`IUnitOfWork`
  (`Nosum.Abstractions/Data`). Add query methods to the service, not raw `IQueryable` to handlers.
- **Unique rules are enforced by the database** (unique index) and a lost race is mapped to a
  normal refusal with `IUnitOfWork.IsUniqueViolation`, not an exception to the client.
- **Time comes from `TimeProvider`**, passed to services; tests use a fake clock.
- **Secrets**: passwords only as `IPasswordHasher` output (PBKDF2-SHA256, self-describing
  string), session tokens only as `ISessionTokenService` hashes. Never log or return a hash.
  Never invent crypto.
- **Connection string** comes from `ConnectionStrings__Default`. Production without it refuses
  to start (`NosumDatabase.ResolveConnectionString`); only non-Production falls back to the
  local `nosum_dev` database. Migrations are applied at host start.
- `Nosum.Infrastructure` is not AOT/trim compatible (EF Core); the other server libraries are.

## Accounts and the current player

- Account packets: `contracts/Nosum.Contracts/Account/` (opcode band `0x1000-0x10FF`).
  Replies carry `AccountStatus` codes only (`i18n`).
- On success a handler calls `connection.AttachPlayer(new PlayerIdentity(id, name))`
  (`Nosum.Handlers/Session`), which stores the identity and raises `Level` to `USER`.
- **Any other handler reads the player with `context.Connection.GetPlayer()`**, gated by
  `[PacketPermission(PermissionLevel.USER)]`. A player id inside a packet is never trusted.
- Public handlers (register, login, resume) declare `PermissionLevel.NONE` and a
  `[PacketRateLimit]`; `PermissionMiddleware` and `RateLimitMiddleware` are registered in
  `Program.BuildApplication`.

## Tests

- Persistence tests run against **real PostgreSQL** (`tests/Nosum.IntegrationTests/Server/PostgresFixture.cs`):
  a throw-away database per test class, created with the shipped migrations. Admin connection:
  `NOSUM_TEST_POSTGRES`, default the local dev server. No server = failing tests, not skipped.
- Local server: `service postgresql start`, role `nosum`, databases `nosum_dev` (see
  `NosumDatabase.DevelopmentConnectionString`).
- Hashing/validation rules are unit tests (`tests/Nosum.UnitTests/Server/`); use a small
  iteration count for the hasher there.
