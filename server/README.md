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

Skills: [`nalix-server-hosting`](../.claude/skills/nalix-server-hosting/SKILL.md),
[`nalix-handlers`](../.claude/skills/nalix-handlers/SKILL.md),
[`gameplay-rules`](../.claude/skills/gameplay-rules/SKILL.md).

```bash
dotnet build server/Nosum.slnx
dotnet run --project server/Nosum.Host
```

Current state: the host registers the Nalix system handlers (time sync, system control) and
the WebSocket listener; no game handlers, persistence or secure handshake yet.
