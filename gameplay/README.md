# gameplay/

Farm rules shared by the server and the client: plots, crops, items, time-based growth,
prices. Pure, deterministic logic — no I/O, no clock reads (time is passed in), no network,
no rendering. The server runs it to decide; the client runs it to predict and display.

References `engine/Nosum.Core` only. Rules: [`gameplay-rules`](../.claude/skills/gameplay-rules/SKILL.md).

```bash
dotnet build gameplay/Nosum.Gameplay.slnx
```

Implemented: `Movement/WalkGrid` (blocking tiles from the map's `Buildings` layer) and
`Movement/Walker` (fixed-step walking with a feet footprint, sliding along walls).
