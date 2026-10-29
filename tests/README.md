# tests/

| Project | Covers | Runs |
| :--- | :--- | :--- |
| `Nosum.UnitTests` | Engine, game rules, contracts — pure .NET, no I/O | in-process |
| `Nosum.IntegrationTests` | Server host over a real WebSocket on a free local port; account services on a real PostgreSQL | in-process server + PostgreSQL (`NOSUM_TEST_POSTGRES`); `WorldInstanceTests`: private farms, public maps, new exits, furniture over real WebSocket clients; `ChunkInterestTests`: two players far apart on Town never receive each other, walking into range spawns and out of range despawns (with hysteresis), publishes reach only the chunk's subscribers, a farm chunk's plots arrive when the chunk joins the interest set; `ChunkLoadTests`: 20 players on Town, bytes per snapshot tick (prints the figures; self-contained so it also runs on the code before chunks); `ChunkTestKit` is the shared host/client/position finder. Sign-ups and resumes are rate limited per address and every test shares one: create accounts through `IAccountService` and pace logins |

`VisitWireTests` (visiting, [ADR-019](../docs/adr/ADR-019-visiting-private-instances.md)): real WebSocket clients on signed-in accounts (made through `TestAccounts`, with the fake verifier) check the allow-list, entering, water-only farming, the farmhouse and bin refusals, kick, the co-op setting and leaving the co-op, an offline owner across a restart, guests and the list limit.

Server tests that matter for money: `SocialServiceTests` (market, transfer and co-op services on a real PostgreSQL with real parallel races) and `CoopLandTests` (farming on co-op land: members, audit, non-member and removed member refused, two members racing for one plot, dissolution) and `WorldInstanceTests.CoopLand_*` (the Town gate, a refused stranger, a kicked member warped out), and `SocialWireTests` (real WebSocket clients: looks,
market, shipping, trade, co-op; `SocialClient` spaces every request because all clients share one address and its rate limits).

`ChatWireTests` and `FriendsMailWireTests` (chat, block list, reports, friends, mail, [ADR-029](../docs/adr/ADR-029-chat-friends-mail.md)) use `SocialLifeClient` and check above all who does not receive a message; `MailServiceTests` race twenty collects of one mail on PostgreSQL.

`browser/` holds Playwright scripts (Node, the globally installed `playwright`; not part of any `.slnx`) run by hand against a published client served over a static server with the host on :57300. `audio-check.js` ([ADR-039](../docs/adr/ADR-039-audio-hooks.md)): no console errors, no `AudioContext` before the first click, one running after it, and footsteps, a tool swing and a panel open/close each play a sound in the game; `chat-check.js` (ADR-029): opening the chat box, sending a line and closing it raise no console or page errors and the line shows; `page-hardening-check.js` (device gate and game-like page behaviour, wasm/README.md): phone profile sees the notice and opens no WebSocket, narrow window overlay, right-click, selection, clipboard and chat input; `audio-module-check.js` drives `audio.js` directly (rain remembered until the gesture, one-shots before it dropped, a track with no file is silence).

xUnit + FluentAssertions. Folders mirror the area under test (`Engine/`, `Gameplay/`, `Server/`, `Tools/`).
Rules: [`testing-style`](../.claude/skills/testing-style/SKILL.md).

```bash
dotnet test tests/Nosum.Tests.slnx
```
