# gameplay/

Farm rules shared by the server and the client: plots, crops, items, time-based growth,
prices. Pure, deterministic logic — no I/O, no clock reads (time is passed in), no network,
no rendering. The server runs it to decide; the client runs it to predict and display.

References `engine/Nosum.Core` only. Rules: [`gameplay-rules`](../.claude/skills/gameplay-rules/SKILL.md).

```bash
dotnet build gameplay/Nosum.Gameplay.slnx
```

No rules are implemented yet.
