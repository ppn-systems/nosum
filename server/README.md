# server/

The Nosum realtime server: one Nalix WebSocket listener (`/ws/`, port 57300) that every client
connects to. Decision: [ADR-003](../docs/adr/ADR-003-server-authoritative-websocket.md).

| Project | Holds | May reference |
| :--- | :--- | :--- |
| `Nosum.Domain` | Persisted entities and enums | `Nosum.Gameplay` |
| `Nosum.Abstractions` | Service interfaces, DTO rows | `Nosum.Domain`, `Nosum.Contracts` |
| `Nosum.Infrastructure` | Persistence, service implementations | `Nosum.Abstractions` |
| `Nosum.Handlers` | Stateless packet handlers, by domain folder | `Nosum.Abstractions`, `Nalix.Runtime` — **never** Infrastructure |
| `Nosum.Host` | Composition root (`Program.cs`) | `Nosum.Handlers`, `Nosum.Infrastructure`, `Nalix.Hosting` |

Persistence and accounts ([ADR-009](../docs/adr/ADR-009-postgresql-persistence-and-accounts.md)):
PostgreSQL through EF Core in `Nosum.Infrastructure/Persistence` (`NosumDbContext`, one
`IEntityTypeConfiguration` per entity, migrations in `Persistence/Migrations`). Features reach data
through `IRepository<T>` / `IUnitOfWork` (`Nosum.Abstractions/Data`) inside a service. Account
handlers are in `Nosum.Handlers/Account`; `connection.GetPlayer()` (`Nosum.Handlers/Session`)
returns the signed-in player of a connection.

Skills: [`data-model-ef-core`](../.claude/skills/data-model-ef-core/SKILL.md),
[`nalix-server-hosting`](../.claude/skills/nalix-server-hosting/SKILL.md),
[`nalix-handlers`](../.claude/skills/nalix-handlers/SKILL.md),
[`gameplay-rules`](../.claude/skills/gameplay-rules/SKILL.md).

```bash
dotnet build server/Nosum.slnx
dotnet run --project server/Nosum.Host
```

## Running locally

1. Start PostgreSQL and create a role and database matching the development fallback
   (`NosumDatabase.DevelopmentConnectionString`: user `nosum`, database `nosum_dev`), or set
   `ConnectionStrings__Default` to your own connection string.
2. `dotnet run --project server/Nosum.Host` applies pending migrations, then listens on `:57300/ws/`.
3. In Production (`DOTNET_ENVIRONMENT=Production`) `ConnectionStrings__Default` is mandatory; the
   host refuses to start without it.

Integration tests need a PostgreSQL server that may create databases: `NOSUM_TEST_POSTGRES`
(admin connection string), default the local development server.

Current state: time sync, system control, account handlers and the WebSocket listener. No secure
handshake yet (run behind TLS, see ADR-009), and no farm handlers.
