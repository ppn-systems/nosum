# contracts/

Types that cross the wire, shared byte-for-byte by the server and the browser client:
opcodes, Nalix packets, DTOs, enums and `NosumProtocol.Version`. **No logic.**

Layout: `Nosum.Contracts/{Domain}/{C2S,S2C}/` plus one `*OpCode` class per domain.
Rules for packet classes: [`nalix-packets`](../.claude/skills/nalix-packets/SKILL.md).

Domains and opcode bands (record a new band here and in its `*OpCode` class):

| Domain | Band | Packets |
| :--- | :--- | :--- |
| Account | `0x1000-0x10FF` | register, login, resume session, set language; replies carry `AccountStatus` codes |
| Player | `0x2000-0x20FF` | join map, move intent (C2S `0x2000-0x203F`); join result, map snapshot, player left, move correction (S2C `0x2040-0x207F`); replies carry `JoinStatus` / `MoveRefusal` codes |

```bash
dotnet build contracts/Nosum.Contracts.slnx
```
