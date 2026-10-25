# tests/

| Project | Covers | Runs |
| :--- | :--- | :--- |
| `Nosum.UnitTests` | Engine, game rules, contracts — pure .NET, no I/O | in-process |
| `Nosum.IntegrationTests` | Server host over a real WebSocket on a free local port; account services on a real PostgreSQL | in-process server + PostgreSQL (`NOSUM_TEST_POSTGRES`); `WorldInstanceTests`: private farms, public maps, new exits, furniture over real WebSocket clients. Sign-ups and resumes are rate limited per address and every test shares one: create accounts through `IAccountService` and pace logins |

`VisitWireTests` (visiting, [ADR-019](../docs/adr/ADR-019-visiting-private-instances.md)): real WebSocket clients on registered accounts (made through the account service) check the allow-list, entering, water-only farming, the farmhouse and bin refusals, kick, the co-op setting and leaving the co-op, an offline owner across a restart, guests and the list limit.

Server tests that matter for money: `SocialServiceTests` (market, transfer and co-op services on a real PostgreSQL with real parallel races) and `SocialWireTests` (real WebSocket clients: looks,
market, shipping, trade, co-op; `SocialClient` spaces every request because all clients share one address and its rate limits).

xUnit + FluentAssertions. Folders mirror the area under test (`Engine/`, `Gameplay/`, `Server/`, `Tools/`).
Rules: [`testing-style`](../.claude/skills/testing-style/SKILL.md).

```bash
dotnet test tests/Nosum.Tests.slnx
```
