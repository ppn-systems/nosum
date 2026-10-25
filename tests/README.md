# tests/

| Project | Covers | Runs |
| :--- | :--- | :--- |
| `Nosum.UnitTests` | Engine, game rules, contracts — pure .NET, no I/O | in-process |
| `Nosum.IntegrationTests` | Server host over a real WebSocket on a free local port; account services on a real PostgreSQL | in-process server + PostgreSQL (`NOSUM_TEST_POSTGRES`); `WorldInstanceTests`: private farms, public maps, new exits, furniture over real WebSocket clients; `ChunkInterestTests`: two players far apart on Town never receive each other, walking into range spawns and out of range despawns (with hysteresis), publishes reach only the chunk's subscribers, a farm chunk's plots arrive when the chunk joins the interest set; `ChunkLoadTests`: 20 players on Town, bytes per snapshot tick (prints the figures; self-contained so it also runs on the code before chunks); `ChunkTestKit` is the shared host/client/position finder. Sign-ups and resumes are rate limited per address and every test shares one: create accounts through `IAccountService` and pace logins |

Server tests that matter for money: `SocialServiceTests` (market, transfer and co-op services on a real PostgreSQL with real parallel races) and `CoopLandTests` (farming on co-op land: members, audit, non-member and removed member refused, two members racing for one plot, dissolution) and `WorldInstanceTests.CoopLand_*` (the Town gate, a refused stranger, a kicked member warped out), and `SocialWireTests` (real WebSocket clients: looks,
market, shipping, trade, co-op; `SocialClient` spaces every request because all clients share one address and its rate limits).

xUnit + FluentAssertions. Folders mirror the area under test (`Engine/`, `Gameplay/`, `Server/`, `Tools/`).
Rules: [`testing-style`](../.claude/skills/testing-style/SKILL.md).

```bash
dotnet test tests/Nosum.Tests.slnx
```
