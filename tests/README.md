# tests/

| Project | Covers | Runs |
| :--- | :--- | :--- |
| `Nosum.UnitTests` | Engine, game rules, contracts — pure .NET, no I/O | in-process |
| `Nosum.IntegrationTests` | Server host over a real WebSocket on a free local port | in-process server |

xUnit + FluentAssertions. Folders mirror the area under test (`Engine/`, `Gameplay/`, `Server/`).
Rules: [`testing-style`](../.claude/skills/testing-style/SKILL.md).

```bash
dotnet test tests/Nosum.Tests.slnx
```
